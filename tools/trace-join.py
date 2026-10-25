# gdb -q -batch -x trace-join.py  (env BG3_PID, BG3_TRACE_LOG)
# Traces the host's join handling: SendRefuseMessage, the AvatarAssignmentData consumer, the drop-in character
# creation enqueue, and whoever reads that queue afterwards. Addresses are link-time VAs from objdump of the native bg3
# (4.1.1.7631656); the PIE base is read from /proc/<pid>/maps.
import gdb, os, re, time

pid = int(os.environ["BG3_PID"])
log = open(os.environ.get("BG3_TRACE_LOG", "/tmp/bg3-trace.log"), "a", buffering=1)
def out(s):
    line = f"[{time.strftime('%H:%M:%S')}] {s}"
    log.write(line + "\n"); gdb.write(line + "\n")

gdb.execute("set pagination off"); gdb.execute("set confirm off"); gdb.execute("set print thread-events off")
gdb.execute("handle SIGPIPE SIGUSR1 SIGUSR2 SIG32 SIG33 SIG34 SIG35 nostop noprint pass")
gdb.execute(f"attach {pid}")
base = None
exe = os.readlink(f"/proc/{pid}/exe")
for l in open(f"/proc/{pid}/maps"):
    p = l.split()
    if len(p) >= 6 and p[-1].endswith("/bg3") and p[2] == "00000000":
        base = int(p[0].split("-")[0], 16); break
if base is None:
    raise SystemExit("nie znalazłem bazy obrazu bg3 w /proc/pid/maps")
out(f"attached pid={pid} exe={exe} base=0x{base:x}")

def u8(a): return int(gdb.parse_and_eval(f"*(unsigned char*)({a})"))
def u32(a): return int(gdb.parse_and_eval(f"*(unsigned int*)({a})"))
def u64(a): return int(gdb.parse_and_eval(f"*(unsigned long*)({a})"))
def reg(r): return int(gdb.parse_and_eval(f"${r}"))
def bt(n=8):
    try:
        fr = gdb.newest_frame(); lines = []
        for _ in range(n):
            if fr is None: break
            pc = fr.pc(); lines.append(f"0x{pc - base:x}" if base <= pc < base + 0x10000000 else f"0x{pc:x}(ext)")
            fr = fr.older()
        return " <- ".join(lines)
    except Exception as e: return f"bt failed: {e}"

ENQ = 0x28e36d0
# esv::character_creation::System: g_Server -> EntityWorld(+0x288) -> systems(+0x30)[TypeId*0xf8]
server = u64(base + 0x7b8c878); world = u64(server + 0x288); systems = u64(world + 0x30)
cc_idx = u32(base + 0x7cef4c8); cc_sys = u64(systems + cc_idx * 0xf8); cc_set = cc_sys + 0x258
out(f"server=0x{server:x} world=0x{world:x} ccSystem=0x{cc_sys:x} (typeIdx {cc_idx}) pendingSet=0x{cc_set:x} count={u32(cc_set+0x2c)}")
seen_pcs = {}

class Refuse(gdb.Breakpoint):
    def stop(self):
        out(f"SendRefuseMessage peer={u32(reg('rcx'))} bt={bt(4)}"); return False
class Candidate(gdb.Breakpoint):
    """inside the avatar picker: one entry per entity with tag::AvatarComponent"""
    def stop(self):
        out(f"  candidate entity=0x{u64(reg('rsi')):x} hasMember={u8(reg('rdx'))} noReservedUser={u8(reg('rcx'))} dismissed={u8(reg('r8'))} v5c=0x{u32(reg('r9')):x} v60=0x{u32(reg('r15')):x} ctrl=0x{u32(reg('rax')):x}")
        return False
class Consume(gdb.Breakpoint):
    def stop(self):
        ad = reg("rdx")
        out(f"ConsumeAssignment entity=0x{u64(ad):x} b8={u8(ad+8)} c={u32(ad+0xc):x} d={u32(ad+0x10):x} e={u32(ad+0x14):x} found={u8(ad+0x18)} needsAssign={u8(ad+0x29)} partyFull={u8(ad+0x28)} disabled={u8(ad+0x2a)} dismiss=0x{u64(ad+0x20):x} peer={u32(reg('rsi'))}")
        return False
class Enqueue(gdb.Breakpoint):
    def stop(self):
        if reg("rdi") != cc_set: return False
        out(f"CC enqueue user=0x{u32(reg('rsi')):x} count_before={u32(cc_set+0x2c)} bt={bt(6)}"); return False
class SetRead(gdb.Breakpoint):
    def __init__(self):
        super().__init__(f"*(int*)0x{cc_set+0x2c:x}", gdb.BP_WATCHPOINT, gdb.WP_READ)
    def stop(self):
        pc = reg("pc") - base
        if ENQ <= pc < ENQ + 0x400: return False
        n = seen_pcs.get(pc, 0) + 1; seen_pcs[pc] = n
        if n <= 2: out(f"CC set READ at 0x{pc:x} (#{n}) count={u32(cc_set+0x2c)} bt={bt(8)}")
        if sum(seen_pcs.values()) > 400: self.enabled = False; out("watchpoint off (limit)")
        return False

class CCUpdate(gdb.Breakpoint):
    """esv::character_creation::System update: log the pending set whenever it is non-empty."""
    def stop(self):
        n = u32(cc_set + 0x2c)
        if n == 0: return False
        users = [hex(u32(u64(cc_sys + 0x278) + 4 * i)) for i in range(n)]
        out(f"CCUpdate pending={users} sessionActive={u8(cc_sys+0x250)} flag288={u8(cc_sys+0x288)} set290count={u32(cc_sys+0x290+0x2c)}")
        return False
class CCStartUser(gdb.Breakpoint):
    def stop(self):
        out(f"CCStartUser user=0x{u32(reg('rsi')):x} isFirst={u8(reg('rdx')) if False else int(gdb.parse_and_eval('$dl'))} bt={bt(5)}"); return False
CCUpdate(f"*0x{base+0x28412b0:x}"); CCStartUser(f"*0x{base+0x427a190:x}")
SetRead()
Refuse(f"*0x{base+0x3f396c0:x}"); Consume(f"*0x{base+0x2f34fb0:x}"); Candidate(f"*0x{base+0x3f3a269:x}"); Enqueue(f"*0x{base+ENQ:x}")
out("breakpoints set, continuing")
while True:
    try: gdb.execute("continue")
    except gdb.error as e: out(f"stopped: {e}"); break
    except KeyboardInterrupt: break
gdb.execute("detach")
