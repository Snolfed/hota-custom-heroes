// Decompiles every function that references a string containing any of the given substrings.
// Usage (headless): -postScript DecompStringRefs.java "*.dll" "Packs"
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.Reference;
import java.util.*;

public class DecompStringRefs extends GhidraScript {
    @Override
    public void run() throws Exception {
        DecompInterface di = new DecompInterface();
        di.openProgram(currentProgram);
        Set<Function> done = new HashSet<>();
        for (Data d : currentProgram.getListing().getDefinedData(true)) {
            Object v = d.getValue();
            if (!(v instanceof String)) continue;
            String s = (String) v;
            boolean match = false;
            for (String needle : getScriptArgs()) if (s.contains(needle)) match = true;
            if (!match) continue;
            for (Reference r : getReferencesTo(d.getAddress())) {
                Function f = getFunctionContaining(r.getFromAddress());
                println("STRING \"" + s + "\" @ " + d.getAddress() + " used in " + (f == null ? r.getFromAddress() : f.getName()));
                if (f == null || !done.add(f)) continue;
                DecompileResults res = di.decompileFunction(f, 60, monitor);
                if (res.decompileCompleted()) println(res.getDecompiledFunction().getC());
            }
        }
    }
}
