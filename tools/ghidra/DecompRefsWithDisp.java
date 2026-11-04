// Decompiles functions that reference address A and whose instructions use all of the given
// memory displacements. Usage (headless): -postScript DecompRefsWithDisp.java 67DCE8 44 24
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.*;
import ghidra.program.model.scalar.Scalar;
import ghidra.program.model.symbol.Reference;
import java.util.*;

public class DecompRefsWithDisp extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        Address addr = toAddr(Long.parseLong(args[0], 16));
        Set<Long> wanted = new HashSet<>();
        for (int i = 1; i < args.length; i++) wanted.add(Long.parseLong(args[i], 16));
        Set<Function> funcs = new LinkedHashSet<>();
        for (Reference r : getReferencesTo(addr)) {
            Function f = getFunctionContaining(r.getFromAddress());
            if (f != null) funcs.add(f);
        }
        DecompInterface di = new DecompInterface();
        di.openProgram(currentProgram);
        println("REFS: " + funcs.size());
        for (Function f : funcs) {
            Set<Long> seen = new HashSet<>();
            for (Instruction ins : currentProgram.getListing().getInstructions(f.getBody(), true)) {
                if (!ins.toString().contains("[")) continue;
                for (int op = 0; op < ins.getNumOperands(); op++)
                    for (Object o : ins.getOpObjects(op))
                        if (o instanceof Scalar && wanted.contains(((Scalar) o).getUnsignedValue()))
                            seen.add(((Scalar) o).getUnsignedValue());
            }
            if (!seen.containsAll(wanted)) continue;
            DecompileResults res = di.decompileFunction(f, 60, monitor);
            println("##### " + f.getEntryPoint() + " " + f.getName());
            if (res.decompileCompleted()) println(res.getDecompiledFunction().getC());
        }
    }
}
