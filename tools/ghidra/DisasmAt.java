// Prints disassembly of the functions containing the given hex addresses.
// Usage (headless, -process): -postScript DisasmAt.java 4E59D0 ...
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.*;

public class DisasmAt extends GhidraScript {
    @Override
    public void run() throws Exception {
        Listing listing = currentProgram.getListing();
        for (String a : getScriptArgs()) {
            Address addr = toAddr(Long.parseLong(a, 16));
            Function f = getFunctionContaining(addr);
            println("===== " + a + " -> " + (f == null ? "NO FUNCTION" : f.getName()));
            if (f == null) continue;
            StringBuilder sb = new StringBuilder();
            for (Instruction ins : listing.getInstructions(f.getBody(), true)) {
                sb.append(ins.getAddress()).append("  ").append(ins).append("\n");
            }
            println(sb.toString());
        }
    }
}
