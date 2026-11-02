// Prints decompiled C for functions at the given hex addresses.
// Usage (headless): -postScript DecompAt.java 4E59D0 4E6260 ...
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DecompAt extends GhidraScript {
    @Override
    public void run() throws Exception {
        DecompInterface di = new DecompInterface();
        di.openProgram(currentProgram);
        for (String a : getScriptArgs()) {
            Address addr = toAddr(Long.parseLong(a, 16));
            Function f = getFunctionContaining(addr);
            if (f == null) f = createFunction(addr, null);
            println("===== " + a + " -> " + (f == null ? "NO FUNCTION" : f.getName() + " @ " + f.getEntryPoint()));
            if (f == null) continue;
            DecompileResults r = di.decompileFunction(f, 60, monitor);
            println(r.decompileCompleted() ? r.getDecompiledFunction().getC() : "decompile failed: " + r.getErrorMessage());
        }
    }
}
