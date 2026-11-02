// Lists functions whose instructions use the given memory displacements, e.g. struct offsets.
// Usage (headless): -postScript FindDisp.java 3D5 41B 101 E5
// Prints, per function, which of the displacements it uses; functions using more of them first.
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.*;
import ghidra.program.model.scalar.Scalar;
import java.util.*;

public class FindDisp extends GhidraScript {
    @Override
    public void run() throws Exception {
        List<Long> wanted = new ArrayList<>();
        for (String a : getScriptArgs()) wanted.add(Long.parseLong(a, 16));
        Map<Function, Set<Long>> hits = new HashMap<>();
        for (Instruction ins : currentProgram.getListing().getInstructions(true)) {
            String text = ins.toString();
            if (!text.contains("[")) continue;
            for (int op = 0; op < ins.getNumOperands(); op++) {
                for (Object o : ins.getOpObjects(op)) {
                    if (!(o instanceof Scalar)) continue;
                    long v = ((Scalar) o).getUnsignedValue();
                    if (!wanted.contains(v)) continue;
                    Function f = getFunctionContaining(ins.getAddress());
                    if (f != null) hits.computeIfAbsent(f, k -> new TreeSet<>()).add(v);
                }
            }
        }
        List<Map.Entry<Function, Set<Long>>> list = new ArrayList<>(hits.entrySet());
        list.sort((x, y) -> y.getValue().size() - x.getValue().size());
        int n = 0;
        for (Map.Entry<Function, Set<Long>> e : list) {
            StringBuilder sb = new StringBuilder();
            for (Long v : e.getValue()) sb.append(Long.toHexString(v)).append(' ');
            println("HIT " + e.getKey().getEntryPoint() + " " + e.getKey().getName() + " : " + sb);
            if (++n >= 60) break;
        }
    }
}
