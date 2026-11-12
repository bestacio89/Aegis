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
 * Validates the stable, engine-neutral semantic fact oracle before either
 * candidate engine is asked to extract facts from the corpus.
 */
public final class SemanticFactCorpusValidator {
    private static final String HEADER =
        "fact_id\tkind\tmodule\tsource_set\tfixture_path\tsource_identity\ttarget_identity\texpectation";
    private static final Pattern FACT_ID = Pattern.compile("JFACT-[0-9]{3}");
    private static final Set<String> REQUIRED_KINDS = Set.of(
        "type-declaration",
        "inheritance",
        "generic-supertype",
        "annotation",
        "constructor-invocation",
        "method-invocation",
        "overload-selection",
        "method-reference",
        "module-requires",
        "module-exports",
        "unresolved-type");

    private SemanticFactCorpusValidator() {
    }

    public static void main(String[] args) throws IOException {
        if (args.length != 2) {
            throw new IllegalArgumentException(
                "Usage: SemanticFactCorpusValidator <fixture-root> <fact-corpus>");
        }

        Path fixtureRoot = Path.of(args[0]).toAbsolutePath().normalize();
        Path corpus = Path.of(args[1]).toAbsolutePath().normalize();
        if (!Files.isDirectory(fixtureRoot) || !Files.isRegularFile(corpus)) {
            throw new IllegalArgumentException("Fixture root or semantic fact corpus does not exist.");
        }

        List<String> lines = Files.readAllLines(corpus);
        if (lines.isEmpty() || !HEADER.equals(lines.getFirst())) {
            throw new IllegalStateException("Semantic fact corpus header is missing or invalid.");
        }

        Set<String> factIds = new HashSet<>();
        Set<String> observedKinds = new HashSet<>();
        int factCount = 0;
        for (int index = 1; index < lines.size(); index++) {
            String line = lines.get(index);
            if (line.isBlank()) {
                continue;
            }

            String[] fields = line.split("\\t", -1);
            if (fields.length != 8) {
                throw new IllegalStateException(
                    "Expected 8 tab-separated fields at corpus line " + (index + 1));
            }

            String factId = fields[0].trim();
            String kind = fields[1].trim();
            String module = fields[2].trim();
            String sourceSet = fields[3].trim();
            String fixturePath = fields[4].trim();
            String sourceIdentity = fields[5].trim();
            String targetIdentity = fields[6].trim();
            String expectation = fields[7].trim();

            if (!FACT_ID.matcher(factId).matches() || !factIds.add(factId)) {
                throw new IllegalStateException(
                    "Fact ID must be stable, unique and match JFACT-NNN: " + factId);
            }
            if (!REQUIRED_KINDS.contains(kind)) {
                throw new IllegalStateException("Unsupported semantic fact kind: " + kind);
            }
            if (module.isEmpty() || !Set.of("production", "test", "generated").contains(sourceSet)) {
                throw new IllegalStateException("Invalid module or source set for " + factId);
            }
            if (sourceIdentity.isEmpty() || targetIdentity.isEmpty()) {
                throw new IllegalStateException("Source and target identities are required for " + factId);
            }
            if (!Set.of("resolved", "unresolved").contains(expectation)) {
                throw new IllegalStateException("Invalid resolution expectation for " + factId);
            }
            if (kind.equals("unresolved-type") != expectation.equals("unresolved")) {
                throw new IllegalStateException(
                    "Only unresolved-type facts may expect unresolved bindings: " + factId);
            }

            Path relativeFixture = Path.of(fixturePath).normalize();
            Path resolvedFixture = fixtureRoot.resolve(relativeFixture).normalize();
            if (relativeFixture.isAbsolute()
                || relativeFixture.startsWith("..")
                || !resolvedFixture.startsWith(fixtureRoot)
                || !fixturePath.endsWith(".java.fixture")
                || !Files.isRegularFile(resolvedFixture)) {
                throw new IllegalStateException(
                    "Fixture path is invalid or missing for " + factId + ": " + fixturePath);
            }

            observedKinds.add(kind);
            factCount++;
        }

        Set<String> missingKinds = new HashSet<>(REQUIRED_KINDS);
        missingKinds.removeAll(observedKinds);
        if (!missingKinds.isEmpty()) {
            throw new IllegalStateException("Semantic fact corpus is missing required kinds: " + missingKinds);
        }
        if (factCount != factIds.size()) {
            throw new IllegalStateException("Semantic fact IDs are not unique.");
        }

        System.out.printf(
            "{\"status\":\"passed\",\"facts\":%d,\"kinds\":%d,\"missingKinds\":%s}%n",
            factCount,
            observedKinds.size(),
            missingKinds.stream().sorted().collect(Collectors.joining(",", "[\"", "\"]")));
    }
}
