// Dumps decompiled C for every non-thunk function to the path given as script arg.
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.listing.*;
import java.io.*;

public class ExportDecomp extends GhidraScript {
    @Override
    public void run() throws Exception {
        String out = getScriptArgs()[0];
        DecompInterface d = new DecompInterface();
        d.openProgram(currentProgram);
        try (PrintWriter w = new PrintWriter(new FileWriter(out))) {
            for (Function f : currentProgram.getFunctionManager().getFunctions(true)) {
                if (f.isThunk() || f.isExternal()) continue;
                DecompileResults r = d.decompileFunction(f, 120, monitor);
                w.println("// ==== " + f.getName(true) + " @ " + f.getEntryPoint());
                w.println(r.decompileCompleted() ? r.getDecompiledFunction().getC() : "// FAILED: " + r.getErrorMessage());
            }
        }
    }
}
