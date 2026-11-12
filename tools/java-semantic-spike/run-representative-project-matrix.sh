#!/usr/bin/env bash
set -euo pipefail

manifest="tools/java-semantic-spike/representative-projects.tsv"
results_directory="artifacts/representative-projects/results"
mkdir -p "$results_directory"

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
  local output="$results_directory/${project_id}-${source_set}-${engine}.jsonl"

  mvn --quiet -f tools/java-semantic-spike/pom.xml compile exec:java@representative-project-benchmark \
    -Dexec.args="$engine $project_id $source_set $source_level $project_root $resolution_roots $parse_roots" \
    | tee "$output"

  jq -e '
    .filesDiscovered > 0
    and .filesParsed == .filesDiscovered
    and .parseFailures == 0
    and .syntaxErrors == 0
    and .environment == "source-roots+jdk-only"
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

  for engine in jdt javaparser; do
    run_benchmark "$engine" main "$source_level" "$main_resolution_roots" "$main_parse_roots"
    run_benchmark "$engine" test "$source_level" "$test_resolution_roots" "$test_parse_roots"
  done

  echo "Representative project passed: $project_id ($license; $build_models; $actual_main_count main/$actual_test_count test Java files)"
done < <(tail -n +2 "$manifest")

report_count="$(find "$results_directory" -maxdepth 1 -type f -name '*.jsonl' | wc -l | tr -d ' ')"
if [[ "$report_count" -ne 12 ]]; then
  echo "Expected 12 representative-project reports, found $report_count" >&2
  exit 1
fi
