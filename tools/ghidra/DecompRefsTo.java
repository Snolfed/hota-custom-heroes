// Decompiles every function that references any of the given hex addresses.
// Usage (headless): -postScript DecompRefsTo.java 679C80 ...
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import java.util.*;

public class DecompRefsTo extends GhidraScript {
    @Override
    public void run() throws Exception {
        DecompInterface di = new DecompInterface();
        di.openProgram(currentProgram);
        Set<Function> done = new LinkedHashSet<>();
        for (String a : getScriptArgs()) {
            Address addr = toAddr(Long.parseLong(a, 16));
            for (Reference r : getReferencesTo(addr)) {
                Function f = getFunctionContaining(r.getFromAddress());
                if (f != null) done.add(f);
            }
        }
        println("FUNCTIONS: " + done.size());
        for (Function f : done) {
            DecompileResults res = di.decompileFunction(f, 60, monitor);
            println("##### " + f.getEntryPoint() + " " + f.getName());
            if (res.decompileCompleted()) println(res.getDecompiledFunction().getC());
        }
    }
}
