#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

manifest="tools/java-semantic-spike/representative-projects.tsv"
results_directory="$repository_root/artifacts/representative-projects/results"
classpath_directory="$repository_root/artifacts/representative-projects/classpaths"
mkdir -p "$results_directory" "$classpath_directory"
find "$results_directory" -maxdepth 1 -type f -name '*.jsonl' -delete
find "$classpath_directory" -maxdepth 1 -type f -name '*.txt' -delete

count_java_files() {
  local roots="$1"
  local total=0
  IFS=',' read -r -a source_roots <<< "$roots"
  for source_root in "${source_roots[@]}"; do
    local count
    count="$(find "$project_root/$source_root" -type f -name '*.java' | wc -l | tr -d ' ')"
    total=$((total + count))
  done
  printf '%s' "$total"
}

run_benchmark() {
  local engine="$1"
  local source_set="$2"
  local source_level="$3"
  local resolution_roots="$4"
  local parse_roots="$5"
  local classpath_file="$6"
  local mode="source-only"
  local options=""
  local expected_environment="source-roots+jdk-only"
  local expected_classpath_entries=0

  if [[ -n "$classpath_file" ]]; then
    mode="curated-classpath"
    options="--classpath-file=$classpath_file"
    expected_environment="source-roots+jdk+curated-external-classpath"
    expected_classpath_entries="$(awk -F ':' '{ print NF }' "$classpath_file")"
  fi

  local output="$results_directory/$project_id-$source_set-$mode-$engine.jsonl"

  mvn --quiet -f tools/java-semantic-spike/pom.xml compile exec:java@representative-project-benchmark \
    -Dexec.args="$engine $project_id $source_set $source_level $project_root $resolution_roots $parse_roots $options" \
    | tee "$output"

  jq -e --arg environment "$expected_environment" --argjson classpathEntries "$expected_classpath_entries" '
    .filesDiscovered > 0
    and .filesParsed == .filesDiscovered
    and .parseFailures == 0
    and .syntaxErrors == 0
    and .environment == $environment
    and .externalClasspathEntries == $classpathEntries
    and .typeReferenceMetric == "outermost-reference-type-nodes-v1"
  ' "$output" > /dev/null
}

while IFS=$'\t' read -r \
  project_id repository commit_sha checkout_path project_subdirectory license license_file \
  source_level build_models main_resolution_roots main_parse_roots \
  test_resolution_roots test_parse_roots expected_main_count expected_test_count; do

  [[ -z "$project_id" ]] && continue

  actual_commit="$(git -C "$checkout_path" rev-parse HEAD)"
  if [[ "$actual_commit" != "$commit_sha" ]]; then
    echo "Pinned commit mismatch for $project_id: expected $commit_sha, got $actual_commit" >&2
    exit 1
  fi

  if ! grep -qi "Apache License" "$checkout_path/$license_file"; then
    echo "Expected Apache-2.0 license evidence is missing for $project_id" >&2
    exit 1
  fi

  project_root="$checkout_path/$project_subdirectory"
  if [[ ! -d "$project_root" ]]; then
    echo "Project subdirectory is missing for $project_id: $project_root" >&2
    exit 1
  fi

  actual_main_count="$(count_java_files "$main_parse_roots")"
  actual_test_count="$(count_java_files "$test_parse_roots")"
  if [[ "$actual_main_count" -ne "$expected_main_count" || "$actual_test_count" -ne "$expected_test_count" ]]; then
    echo "Source inventory changed for $project_id: main=$actual_main_count/$expected_main_count test=$actual_test_count/$expected_test_count" >&2
    exit 1
  fi

  for source_root in ${main_resolution_roots//,/ }; do
    [[ -d "$project_root/$source_root" ]] || {
      echo "Missing main resolution root for $project_id: $source_root" >&2
      exit 1
    }
  done
  for source_root in ${test_resolution_roots//,/ }; do
    [[ -d "$project_root/$source_root" ]] || {
      echo "Missing test resolution root for $project_id: $source_root" >&2
      exit 1
    }
  done

  classpath_pom="$repository_root/tools/java-semantic-spike/representative-classpaths/$project_id/pom.xml"
  main_classpath_file="$classpath_directory/$project_id-main.txt"
  test_classpath_file="$classpath_directory/$project_id-test.txt"
  if [[ ! -f "$classpath_pom" ]]; then
    echo "Representative curated classpath POM is missing for $project_id" >&2
    exit 1
  fi

  mvn --batch-mode --no-transfer-progress --quiet \
    -f "$classpath_pom" \
    org.apache.maven.plugins:maven-dependency-plugin:3.8.1:build-classpath \
    -DincludeScope=compile \
    -Dmdep.outputFile="$main_classpath_file"
  mvn --batch-mode --no-transfer-progress --quiet \
    -f "$classpath_pom" \
    org.apache.maven.plugins:maven-dependency-plugin:3.8.1:build-classpath \
    -DincludeScope=test \
    -Dmdep.outputFile="$test_classpath_file"

  if [[ ! -s "$main_classpath_file" || ! -s "$test_classpath_file" ]]; then
    echo "Resolved representative main/test classpath is empty for $project_id" >&2
    exit 1
  fi
  for resolved_classpath in "$main_classpath_file" "$test_classpath_file"; do
    if grep -Eiq 'org\.eclipse\.jdt\.core|javaparser-(core|symbol-solver-core)' "$resolved_classpath"; then
      echo "Candidate engine dependency leaked into the $project_id analysis classpath" >&2
      exit 1
    fi
  done

  case "$project_id" in
    spring-petclinic)
      grep -Eq 'jakarta.persistence-api-[^/]+\.jar' "$main_classpath_file"
      grep -Eq 'spring-core-[^/]+\.jar' "$main_classpath_file"
      grep -Eq 'spring-test-[^/]+\.jar' "$test_classpath_file"
      grep -Eq 'junit-jupiter-api-[^/]+\.jar' "$test_classpath_file"
      grep -Eq 'testcontainers-junit-jupiter-[^/]+\.jar' "$test_classpath_file"
      ;;
    quarkus-getting-started)
      grep -Eq 'jakarta.ws.rs-api-[^/]+\.jar' "$main_classpath_file"
      grep -Eq 'quarkus-junit-[^/]+\.jar' "$test_classpath_file"
      grep -Eq 'rest-assured-[^/]+\.jar' "$test_classpath_file"
      ;;
    micronaut-starter-aws-cdk)
      grep -Fq 'aws-cdk-lib-2.243.0.jar' "$main_classpath_file"
      grep -Eq 'constructs-[^/]+\.jar' "$main_classpath_file"
      grep -Fq 'aws-cdk-lib-2.243.0.jar' "$test_classpath_file"
      grep -Eq 'constructs-[^/]+\.jar' "$test_classpath_file"
      ;;
    *)
      echo "No representative classpath contract is defined for $project_id" >&2
      exit 1
      ;;
  esac

  for engine in jdt javaparser; do
    run_benchmark "$engine" main "$source_level" "$main_resolution_roots" "$main_parse_roots" ""
    run_benchmark "$engine" test "$source_level" "$test_resolution_roots" "$test_parse_roots" ""
    run_benchmark "$engine" main "$source_level" "$main_resolution_roots" "$main_parse_roots" "$main_classpath_file"
    run_benchmark "$engine" test "$source_level" "$test_resolution_roots" "$test_parse_roots" "$test_classpath_file"
  done

  echo "Representative project passed: $project_id ($license; $build_models; $actual_main_count main/$actual_test_count test Java files; source-only and curated main/test classpaths)"
done < <(tail -n +2 "$manifest")

report_count="$(find "$results_directory" -maxdepth 1 -type f -name '*.jsonl' | wc -l | tr -d ' ')"
if [[ "$report_count" -ne 24 ]]; then
  echo "Expected 24 representative-project reports, found $report_count" >&2
  exit 1
fi
