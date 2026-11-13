package org.aegis.spike;

import java.io.BufferedReader;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.CompletableFuture;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Runs isolated, repeatable candidate-engine trials and samples Linux peak RSS.
 * This is an evaluation harness, not a production Aegis component.
 */
public final class SemanticPerformanceBenchmark {
    private static final Pattern ANALYSIS_MILLIS =
        Pattern.compile("\"elapsedMilliseconds\":(\\d+)");
    private static final Pattern SOURCE_FILES =
        Pattern.compile("\"sourceFiles\":(\\d+)");
    private static final Pattern PARSED_FILES =
        Pattern.compile("\"parsedFiles\":(\\d+)");
    private static final Pattern DIAGNOSTICS =
        Pattern.compile("\"diagnostics\":(\\d+)");
    private static final Pattern RESOLUTION_ATTEMPTS =
        Pattern.compile("\"resolutionAttempts\":(\\d+)");
    private static final Pattern RESOLVED_BINDINGS =
        Pattern.compile("\"resolvedBindings\":(\\d+)");
    private static final Pattern UNEXPECTED_MISSING =
        Pattern.compile("\"unexpectedlyResolvedMissingTypes\":(\\d+)");
    private static final String[] ENGINES = {"jdt", "javaparser"};
    private static final String[] MODES = {"source-only", "external-classpath"};

    private SemanticPerformanceBenchmark() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length != 6) {
            throw new IllegalArgumentException(
                "Usage: SemanticPerformanceBenchmark <fixture-root> <external-classpath-file|-> "
                    + "<runtime-classpath-file> <warmups> <iterations> <report-file>");
        }
        requireLinuxPeakRssSupport();

        Path workingDirectory = Path.of("").toAbsolutePath().normalize();
        Path repositoryRoot = Files.isDirectory(workingDirectory.resolve("tests"))
            ? workingDirectory
            : workingDirectory.getParent();
        if (repositoryRoot == null || !Files.isDirectory(repositoryRoot.resolve("tests"))) {
            throw new IllegalStateException("Unable to locate the Aegis repository root.");
        }

        Path fixtureRoot = resolvePath(args[0], repositoryRoot);
        Path externalClasspath = args[1].equals("-")
            ? null
            : resolvePath(args[1], repositoryRoot);
        Path runtimeClasspathFile = resolvePath(args[2], repositoryRoot);
        int warmups = Integer.parseInt(args[3]);
        int iterations = Integer.parseInt(args[4]);
        Path reportFile = resolvePath(args[5], repositoryRoot);

        if (!Files.isDirectory(fixtureRoot)) {
            throw new IllegalArgumentException("Fixture root does not exist: " + fixtureRoot);
        }
        if (externalClasspath != null && !Files.isRegularFile(externalClasspath)) {
            throw new IllegalArgumentException("External classpath file does not exist: " + externalClasspath);
        }
        if (!Files.isRegularFile(runtimeClasspathFile)) {
            throw new IllegalArgumentException("Runtime classpath file does not exist: " + runtimeClasspathFile);
        }
        if (warmups < 1 || iterations < 5) {
            throw new IllegalArgumentException("At least one warm-up and five measured iterations are required.");
        }

        String runtimeClasspath = Files.readString(runtimeClasspathFile).trim();
        if (runtimeClasspath.isEmpty()) {
            throw new IllegalArgumentException("Runtime classpath file is empty.");
        }

        Path classesDirectory = repositoryRoot.resolve("tools/java-semantic-spike/target/classes")
            .toAbsolutePath().normalize();
        if (!Files.isDirectory(classesDirectory)) {
            throw new IllegalStateException("Compile the semantic spike before running its performance harness.");
        }
        String childClasspath = classesDirectory + java.io.File.pathSeparator + runtimeClasspath;

        List<String> report = new ArrayList<>();
        report.add(environmentRecord(warmups, iterations));
        for (String mode : MODES) {
            Path modeClasspath = mode.equals("source-only") ? null : externalClasspath;
            if (modeClasspath == null && mode.equals("external-classpath")) {
                throw new IllegalArgumentException("External-classpath mode requires its resolved classpath file.");
            }

            for (int trial = -warmups; trial < iterations; trial++) {
                String[] order = Math.floorMod(trial, 2) == 0
                    ? ENGINES
                    : new String[] {"javaparser", "jdt"};
                for (String engine : order) {
                    Trial result = runTrial(
                        engine, mode, fixtureRoot, modeClasspath, childClasspath, trial >= 0, trial);
                    if (trial >= 0) {
                        report.add(result.toJson());
                    }
                }
            }

            List<Trial> jdtSamples = runSamples("jdt", mode, iterations);
            List<Trial> javaParserSamples = runSamples("javaparser", mode, iterations);
            validateComparableWorkloads(jdtSamples, javaParserSamples);
            report.add(summaryRecord("jdt", mode, jdtSamples));
            report.add(summaryRecord("javaparser", mode, javaParserSamples));
        }

        Files.createDirectories(reportFile.getParent());
        Files.write(reportFile, report);
        report.forEach(System.out::println);
    }

    private static List<Trial> runSamples(String engine, String mode, int iterations) {
        return TrialRegistry.take(engine, mode, iterations);
    }

    private static Trial runTrial(
        String engine, String mode, Path fixtureRoot, Path externalClasspath,
        String childClasspath, boolean measured, int trialNumber) throws Exception {
        List<String> command = new ArrayList<>();
        command.add(Path.of(System.getProperty("java.home"), "bin", "java").toString());
        command.add("-Xms64m");
        command.add("-Xmx1g");
        command.add("-XX:+UseG1GC");
        command.add("-cp");
        command.add(childClasspath);
        command.add(SemanticEngineBenchmark.class.getName());
        command.add(engine);
        command.add(fixtureRoot.toString());
        if (externalClasspath != null) {
            command.add(externalClasspath.toString());
        }

        long started = System.nanoTime();
        Process process = new ProcessBuilder(command).redirectErrorStream(true).start();
        long pid = process.pid();
        CompletableFuture<List<String>> output = CompletableFuture.supplyAsync(() -> {
            try (BufferedReader reader = process.inputReader()) {
                return reader.lines().toList();
            } catch (IOException exception) {
                throw new IllegalStateException("Unable to read child benchmark output.", exception);
            }
        });

        long peakResidentBytes = 0;
        while (process.isAlive()) {
            peakResidentBytes = Math.max(peakResidentBytes, readPeakResidentBytes(process));
            Thread.sleep(10);
        }
        int exitCode = process.waitFor();
        long processMillis = Duration.ofNanos(System.nanoTime() - started).toMillis();
        List<String> lines = output.join();
        if (exitCode != 0) {
            throw new IllegalStateException("Child benchmark failed (" + engine + ", " + mode
                + ", trial " + trialNumber + ", exit " + exitCode + "): " + String.join("\\n", lines));
        }
        if (peakResidentBytes == 0) {
            throw new IllegalStateException("Peak resident memory could not be sampled for child PID " + pid);
        }

        String summary = lines.stream()
            .filter(line -> line.startsWith("{") && line.contains("\"elapsedMilliseconds\""))
            .reduce((first, second) -> second)
            .orElseThrow(() -> new IllegalStateException(
                "Child benchmark emitted no metrics JSON: " + String.join("\\n", lines)));

        Trial trial = new Trial(
            engine,
            mode,
            trialNumber,
            requiredLong(summary, ANALYSIS_MILLIS, "elapsedMilliseconds"),
            processMillis,
            peakResidentBytes,
            exitCode,
            requiredLong(summary, SOURCE_FILES, "sourceFiles"),
            requiredLong(summary, PARSED_FILES, "parsedFiles"),
            requiredLong(summary, DIAGNOSTICS, "diagnostics"),
            requiredLong(summary, RESOLUTION_ATTEMPTS, "resolutionAttempts"),
            requiredLong(summary, RESOLVED_BINDINGS, "resolvedBindings"),
            requiredLong(summary, UNEXPECTED_MISSING, "unexpectedlyResolvedMissingTypes"));
        if (trial.parsedFiles() != trial.sourceFiles()) {
            throw new IllegalStateException("Candidate did not parse every fixture file: " + trial.toJson());
        }
        if (trial.unexpectedlyResolvedMissingTypes() != 0) {
            throw new IllegalStateException("Candidate resolved intentionally missing types: " + trial.toJson());
        }
        if (measured) {
            TrialRegistry.add(trial);
        }
        return trial;
    }

    private static Path resolvePath(String value, Path repositoryRoot) {
        Path path = Path.of(value);
        return (path.isAbsolute() ? path : repositoryRoot.resolve(path)).normalize();
    }

    private static long readPeakResidentBytes(Process process) throws IOException {
        if (!process.isAlive()) {
            return 0;
        }

        Path status = Path.of("/proc", Long.toString(process.pid()), "status");
        try {
            if (!Files.isRegularFile(status)) {
                return 0;
            }
            try (var lines = Files.lines(status)) {
                return lines.filter(line -> line.startsWith("VmHWM:"))
                    .findFirst()
                    .map(line -> {
                        String[] fields = line.trim().split("\\s+");
                        return Long.parseLong(fields[1]) * 1024;
                    })
                    .orElse(0L);
            }
        } catch (IOException samplingFailure) {
            if (!process.isAlive()) {
                return 0;
            }
            throw samplingFailure;
        }
    }

    private static long requiredLong(String json, Pattern pattern, String field) {
        Matcher matcher = pattern.matcher(json);
        if (!matcher.find()) {
            throw new IllegalStateException("Child metrics JSON is missing " + field + ": " + json);
        }
        return Long.parseLong(matcher.group(1));
    }

    private static String environmentRecord(int warmups, int iterations) {
        return "{\"recordType\":\"environment\",\"javaVersion\":\""
            + System.getProperty("java.version") + "\",\"os\":\""
            + System.getProperty("os.name") + "\",\"architecture\":\""
            + System.getProperty("os.arch") + "\",\"processors\":"
            + Runtime.getRuntime().availableProcessors() + ",\"warmups\":" + warmups
            + ",\"iterations\":" + iterations
            + ",\"jvmArguments\":\"-Xms64m -Xmx1g -XX:+UseG1GC\",\"rssSource\":\"Linux /proc VmHWM\"}";
    }

    private static void validateComparableWorkloads(List<Trial> jdt, List<Trial> javaParser) {
        if (jdt.size() != javaParser.size()) {
            throw new IllegalStateException("Candidate sample counts differ.");
        }
        long expectedFiles = jdt.getFirst().sourceFiles();
        long expectedParsed = jdt.getFirst().parsedFiles();
        for (Trial sample : jdt) {
            if (sample.sourceFiles() != expectedFiles || sample.parsedFiles() != expectedParsed) {
                throw new IllegalStateException("JDT workload changed between trials.");
            }
        }
        for (Trial sample : javaParser) {
            if (sample.sourceFiles() != expectedFiles || sample.parsedFiles() != expectedParsed) {
                throw new IllegalStateException("JavaParser did not receive the same fully parsed source workload.");
            }
        }
    }

    private static String summaryRecord(String engine, String mode, List<Trial> samples) {
        if (samples.size() < 5) {
            throw new IllegalStateException("Insufficient measured samples for " + engine + "/" + mode);
        }
        return "{\"recordType\":\"summary\",\"engine\":\"" + engine
            + "\",\"mode\":\"" + mode + "\",\"sampleCount\":" + samples.size()
            + ",\"analysisMedianMs\":" + percentile(samples, Trial::analysisMillis, 0.50)
            + ",\"analysisP95Ms\":" + percentile(samples, Trial::analysisMillis, 0.95)
            + ",\"processMedianMs\":" + percentile(samples, Trial::processMillis, 0.50)
            + ",\"processP95Ms\":" + percentile(samples, Trial::processMillis, 0.95)
            + ",\"peakRssMedianBytes\":" + percentile(samples, Trial::peakResidentBytes, 0.50)
            + ",\"peakRssP95Bytes\":" + percentile(samples, Trial::peakResidentBytes, 0.95)
            + ",\"sourceFiles\":" + samples.getFirst().sourceFiles()
            + ",\"parsedFiles\":" + samples.getFirst().parsedFiles()
            + ",\"resolutionAttempts\":" + samples.getFirst().resolutionAttempts()
            + ",\"resolvedBindings\":" + samples.getFirst().resolvedBindings() + "}";
    }

    private static long percentile(List<Trial> samples, java.util.function.ToLongFunction<Trial> metric,
                                   double percentile) {
        List<Long> values = samples.stream().mapToLong(metric).boxed().sorted().toList();
        int index = Math.max(0, (int) Math.ceil(percentile * values.size()) - 1);
        return values.get(index);
    }

    private static void requireLinuxPeakRssSupport() {
        if (!System.getProperty("os.name").toLowerCase(Locale.ROOT).contains("linux")
            || !Files.isDirectory(Path.of("/proc/self"))) {
            throw new IllegalStateException(
                "Repeatable peak-RSS measurement currently requires Linux /proc VmHWM.");
        }
    }

    private record Trial(
        String engine, String mode, int trialNumber,
        long analysisMillis, long processMillis, long peakResidentBytes, int exitCode,
        long sourceFiles, long parsedFiles, long diagnostics,
        long resolutionAttempts, long resolvedBindings, long unexpectedlyResolvedMissingTypes) {
        private String toJson() {
            return "{\"recordType\":\"trial\",\"engine\":\"" + engine
                + "\",\"mode\":\"" + mode + "\",\"trial\":" + trialNumber
                + ",\"analysisMillis\":" + analysisMillis + ",\"processMillis\":" + processMillis
                + ",\"peakResidentBytes\":" + peakResidentBytes + ",\"exitCode\":" + exitCode
                + ",\"sourceFiles\":" + sourceFiles
                + ",\"parsedFiles\":" + parsedFiles + ",\"diagnostics\":" + diagnostics
                + ",\"resolutionAttempts\":" + resolutionAttempts + ",\"resolvedBindings\":"
                + resolvedBindings + ",\"unexpectedlyResolvedMissingTypes\":"
                + unexpectedlyResolvedMissingTypes + "}";
        }
    }

    private static final class TrialRegistry {
        private static final List<Trial> SAMPLES = new ArrayList<>();

        private static synchronized void add(Trial trial) {
            SAMPLES.add(trial);
        }

        private static synchronized List<Trial> take(String engine, String mode, int expected) {
            List<Trial> selected = SAMPLES.stream()
                .filter(sample -> sample.engine().equals(engine) && sample.mode().equals(mode))
                .sorted(Comparator.comparingInt(Trial::trialNumber))
                .toList();
            if (selected.size() != expected) {
                throw new IllegalStateException("Expected " + expected + " samples for "
                    + engine + "/" + mode + " but found " + selected.size());
            }
            return selected;
        }
    }
}
