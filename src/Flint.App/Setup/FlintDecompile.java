import jadx.api.JadxArgs;
import jadx.api.JadxDecompiler;
import jadx.api.JavaClass;
import java.io.File;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Decompiles only what Flint's catalog reads, with jadx's own API: every R class (they name the resources), the given
 * root classes, and what they reference, to the given depth. Resources are decoded in full. The CLI decompiles all
 * 21,000 classes of the Spark app in 40 s; this, about a thousand in 7 s. Same settings, same file layout.
 *
 * Usage: java -cp jadx-all.jar:. FlintDecompile apk output threads depth root... (a root ending in * is a prefix)
 *
 * Flint embeds the compiled class, so a Java runtime without a compiler can run it. After changing this file:
 *   javac --release 11 -cp jadx-<version>-all.jar FlintDecompile.java
 */
public final class FlintDecompile {
    public static void main(String[] a) throws Exception {
        var args = new JadxArgs();
        args.setInputFile(new File(a[0]));
        args.setOutDir(new File(a[1]));
        args.setThreadsCount(Integer.parseInt(a[2]));
        args.setShowInconsistentCode(true); // --show-bad-code
        args.setDeobfuscationMinLength(2); // --deobf-min 2
        var depth = Integer.parseInt(a[3]);
        var roots = List.of(a).subList(4, a.length);

        try (var jadx = new JadxDecompiler(args)) {
            jadx.load();
            jadx.saveResources();
            var chosen = new LinkedHashSet<JavaClass>();
            var frontier = new ArrayList<JavaClass>();
            for (var c : jadx.getClasses()) {
                if (c.getName().equals("R") || c.getName().startsWith("R$")) chosen.add(c);
                else if (roots.stream().anyMatch(r -> r.endsWith("*") ? c.getFullName().startsWith(r.substring(0, r.length() - 1)) : c.getFullName().equals(r))) frontier.add(c);
            }
            chosen.addAll(frontier);
            for (var d = 0; d < depth; d++) {
                var next = new ArrayList<JavaClass>();
                for (var c : frontier)
                    for (var dependency : c.getDependencies())
                        if (chosen.add(dependency.getTopParentClass())) next.add(dependency.getTopParentClass());
                frontier = next;
            }

            var sources = Path.of(a[1], "sources");
            var done = new AtomicInteger();
            // One by one: in parallel, what jadx names a class or infers for a method depends on which classes it has
            // already done, so a run could differ from the last. In order, every run writes the same files.
            chosen.forEach(c -> {
                try {
                    var file = sources.resolve(c.getClassNode().getClassInfo().getAliasFullPath() + ".java");
                    Files.createDirectories(file.getParent());
                    Files.writeString(file, c.getCode() + System.lineSeparator());
                } catch (Exception e) {
                    throw new RuntimeException(c.getFullName(), e);
                }
                System.out.println("progress: " + done.incrementAndGet() + " of " + chosen.size()); // as the CLI says it
            });
        }
    }
}
