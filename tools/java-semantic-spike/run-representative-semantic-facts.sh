#!/usr/bin/env bash
set -euo pipefail

manifest="tools/java-semantic-spike/representative-projects.tsv"
facts_directory="tools/java-semantic-spike/representative-semantic-facts"
results_directory="artifacts/representative-projects/facts"
mkdir -p "$results_directory"

while IFS=$'\t' read -r \
  project_id repository commit_sha checkout_path project_subdirectory license license_file \
  source_level build_models main_resolution_roots main_parse_roots \
  test_resolution_roots test_parse_roots expected_main_count expected_test_count; do

  [[ -z "$project_id" ]] && continue

  project_root="$checkout_path/$project_subdirectory"
  corpus="$facts_directory/$project_id.tsv"
  if [[ ! -d "$project_root" || ! -f "$corpus" ]]; then
    echo "Representative semantic fact inputs are missing for $project_id" >&2
    exit 1
  fi

  for engine in jdt javaparser; do
    output="$results_directory/$project_id-$engine.jsonl"
    mvn --quiet -f tools/java-semantic-spike/pom.xml compile exec:java@semantic-fact-benchmark \
      -Dexec.args="$engine $project_root $corpus --project-root --report-only" \
      | tee "$output"
  done
done < <(tail -n +2 "$manifest")

report_count="$(find "$results_directory" -maxdepth 1 -type f -name '*.jsonl' | wc -l | tr -d ' ')"
if [[ "$report_count" -ne 6 ]]; then
  echo "Expected six representative semantic fact reports, found $report_count" >&2
  exit 1
fi
