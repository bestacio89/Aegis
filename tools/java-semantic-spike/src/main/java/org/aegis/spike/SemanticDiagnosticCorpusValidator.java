package org.aegis.spike;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.regex.Pattern;
import java.util.stream.Collectors;

/**
 * Validates the engine-neutral diagnostic scenarios before candidate-specific
 * diagnostic extraction and normalization are evaluated.
 */
public final class SemanticDiagnosticCorpusValidator {
    private static final String HEADER =
        "case_id\tcategory\tseverity\tfixture_path\tsource_level\ttrigger";
    private static final Pattern CASE_ID = Pattern.compile("JDIAG-[0-9]{3}");
    private static final Set<String> REQUIRED_CATEGORIES = Set.of(
        "syntax-error",
        "compiler-error",
        "unresolved-symbol",
        "incomplete-environment",
        "unsupported-construct",
        "warning",
        "infrastructure-failure");

    private SemanticDiagnosticCorpusValidator() {
    }

    public static void main(String[] args) throws IOException {
        if (args.length != 2) {
            throw new IllegalArgumentException(
                "Usage: SemanticDiagnosticCorpusValidator <fixture-root> <diagnostic-corpus>");
        }

        Path fixtureRoot = Path.of(args[0]).toAbsolutePath().normalize();
        Path corpus = Path.of(args[1]).toAbsolutePath().normalize();
        if (!Files.isDirectory(fixtureRoot) || !Files.isRegularFile(corpus)) {
            throw new IllegalArgumentException("Fixture root or diagnostic corpus does not exist.");
        }

        List<String> lines = Files.readAllLines(corpus);
        if (lines.isEmpty() || !HEADER.equals(lines.getFirst())) {
            throw new IllegalStateException("Semantic diagnostic corpus header is missing or invalid.");
        }

        Set<String> caseIds = new HashSet<>();
        Set<String> observedCategories = new HashSet<>();
        int caseCount = 0;
        for (int index = 1; index < lines.size(); index++) {
            String line = lines.get(index);
            if (line.isBlank()) {
                continue;
            }

            String[] fields = line.split("\\t", -1);
            if (fields.length != 6) {
                throw new IllegalStateException(
                    "Expected 6 tab-separated fields at diagnostic corpus line " + (index + 1));
            }

            String caseId = fields[0].trim();
            String category = fields[1].trim();
            String severity = fields[2].trim();
            String fixturePath = fields[3].trim();
            String sourceLevel = fields[4].trim();
            String trigger = fields[5].trim();

            if (!CASE_ID.matcher(caseId).matches() || !caseIds.add(caseId)) {
                throw new IllegalStateException(
                    "Diagnostic case ID must be stable, unique and match JDIAG-NNN: " + caseId);
            }
            if (!REQUIRED_CATEGORIES.contains(category)) {
                throw new IllegalStateException("Unsupported diagnostic category: " + category);
            }
            String expectedSeverity = category.equals("warning") ? "warning" : "error";
            if (!expectedSeverity.equals(severity)) {
                throw new IllegalStateException(
                    "Severity " + severity + " is inconsistent with category " + category);
            }
            if (!Set.of("17", "21").contains(sourceLevel) || trigger.isEmpty()) {
                throw new IllegalStateException("Invalid source level or trigger for " + caseId);
            }

            if (category.equals("infrastructure-failure")) {
                if (!fixturePath.equals("-") || !trigger.equals("missing-input-path")) {
                    throw new IllegalStateException(
                        "Infrastructure scenario must use the controlled missing-input-path trigger.");
                }
            } else {
                Path relativeFixture = Path.of(fixturePath).normalize();
                Path resolvedFixture = fixtureRoot.resolve(relativeFixture).normalize();
                if (relativeFixture.isAbsolute()
                    || relativeFixture.startsWith("..")
                    || !resolvedFixture.startsWith(fixtureRoot)
                    || !fixturePath.endsWith(".java.fixture")
                    || !Files.isRegularFile(resolvedFixture)) {
                    throw new IllegalStateException(
                        "Diagnostic fixture path is invalid or missing for "
                            + caseId + ": " + fixturePath);
                }
            }

            observedCategories.add(category);
            caseCount++;
        }

        Set<String> missingCategories = new HashSet<>(REQUIRED_CATEGORIES);
        missingCategories.removeAll(observedCategories);
        if (!missingCategories.isEmpty()) {
            throw new IllegalStateException(
                "Diagnostic corpus is missing categories: " + missingCategories);
        }

        String missingCategoriesJson = missingCategories.stream()
            .sorted()
            .map(category -> "\"" + category + "\"")
            .collect(Collectors.joining(",", "[", "]"));
        System.out.printf(
            "{\"status\":\"passed\",\"cases\":%d,\"categories\":%d,\"missingCategories\":%s}%n",
            caseCount,
            observedCategories.size(),
            missingCategoriesJson);
    }
}
