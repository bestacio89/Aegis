package org.aegis.spike;

import java.io.File;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * Reads and validates a resolved external classpath snapshot for isolated
 * semantic-engine evaluation. The snapshot is data; no project build is run.
 */
public final class ClasspathSnapshot {
    private ClasspathSnapshot() {
    }

    public static List<Path> read(Path classpathFile) throws IOException {
        Path normalizedFile = classpathFile.toAbsolutePath().normalize();
        if (!Files.isRegularFile(normalizedFile)) {
            throw new IllegalArgumentException(
                "External classpath file does not exist: " + normalizedFile);
        }

        String value = Files.readString(normalizedFile).trim();
        if (value.isEmpty()) {
            throw new IllegalArgumentException(
                "External classpath file is empty: " + normalizedFile);
        }

        List<Path> entries = new ArrayList<>();
        Set<Path> uniqueEntries = new HashSet<>();
        for (String item : value.split(java.util.regex.Pattern.quote(File.pathSeparator), -1)) {
            if (item.isBlank()) {
                throw new IllegalArgumentException("External classpath contains an empty entry.");
            }
            Path entry = Path.of(item).toAbsolutePath().normalize().toRealPath();
            if (!Files.isRegularFile(entry)
                || !entry.getFileName().toString().toLowerCase(Locale.ROOT).endsWith(".jar")) {
                throw new IllegalArgumentException(
                    "External classpath entry is not a JAR file: " + entry);
            }
            if (!uniqueEntries.add(entry)) {
                throw new IllegalArgumentException(
                    "External classpath contains a duplicate JAR: " + entry);
            }
            entries.add(entry);
        }
        if (entries.isEmpty()) {
            throw new IllegalArgumentException("External classpath must contain at least one JAR.");
        }
        return List.copyOf(entries);
    }

    public static void rejectCandidateEngines(List<Path> entries) {
        for (Path entry : entries) {
            String fileName = entry.getFileName().toString().toLowerCase(Locale.ROOT);
            if (fileName.startsWith("org.eclipse.jdt.core-")
                || fileName.startsWith("javaparser-core-")
                || fileName.startsWith("javaparser-symbol-solver-core-")) {
                throw new IllegalArgumentException(
                    "Candidate engine artifact leaked into the analysed project classpath: " + entry);
            }
        }
    }
}
