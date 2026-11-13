#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

manifest="$repository_root/tools/java-semantic-spike/representative-projects.tsv"
facts_directory="$repository_root/tools/java-semantic-spike/representative-semantic-facts"
classpath_directory="$repository_root/artifacts/representative-projects/fact-classpaths"
results_directory="$repository_root/artifacts/representative-projects/facts"

rm -rf "$classpath_directory" "$results_directory"
mkdir -p "$classpath_directory" "$results_directory"

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
  corpus="$facts_directory/$project_id.tsv"
  classpath_pom="tools/java-semantic-spike/representative-classpaths/$project_id/pom.xml"
  classpath_file="$classpath_directory/$project_id.txt"
  if [[ ! -d "$project_root" || ! -f "$corpus" || ! -f "$classpath_pom" ]]; then
    echo "Representative semantic fact or classpath inputs are missing for $project_id" >&2
    exit 1
  fi

  mvn --batch-mode --no-transfer-progress --quiet \
    -f "$classpath_pom" \
    org.apache.maven.plugins:maven-dependency-plugin:3.8.1:build-classpath \
    -DincludeScope=compile \
    -Dmdep.outputFile="$classpath_file"

  if [[ ! -s "$classpath_file" ]]; then
    echo "Resolved representative compile classpath is empty for $project_id" >&2
    exit 1
  fi
  if grep -Eiq 'org\.eclipse\.jdt\.core|javaparser-symbol-solver-core' "$classpath_file"; then
    echo "Candidate engine dependency leaked into the $project_id analysis classpath" >&2
    exit 1
  fi

  case "$project_id" in
    spring-petclinic)
      grep -Eq 'jakarta.persistence-api-[^/]+\.jar' "$classpath_file"
      grep -Eq 'spring-core-[^/]+\.jar' "$classpath_file"
      ;;
    quarkus-getting-started)
      grep -Eq 'jakarta.ws.rs-api-[^/]+\.jar' "$classpath_file"
      ;;
    micronaut-starter-aws-cdk)
      grep -Fq 'aws-cdk-lib-2.243.0.jar' "$classpath_file"
      grep -Eq 'constructs-[^/]+\.jar' "$classpath_file"
      ;;
    *)
      echo "No representative classpath contract is defined for $project_id" >&2
      exit 1
      ;;
  esac

  expected_fact_count="$(awk -F '\t' 'NR > 1 && NF > 1 && $1 != "" { count++ } END { print count + 0 }' "$corpus")"
  if [[ "$expected_fact_count" -lt 1 ]]; then
    echo "Representative semantic fact corpus is empty for $project_id" >&2
    exit 1
  fi

  for engine in jdt javaparser; do
    output="$results_directory/$project_id-$engine.jsonl"
    debug_output="$results_directory/$project_id-$engine-resolution.log"
    options="--project-root --classpath-file=$classpath_file"
    if [[ "$engine" == "javaparser" && "$project_id" == "micronaut-starter-aws-cdk" ]]; then
      options="$options --report-only --debug-resolution"
    fi

    mvn --quiet -f tools/java-semantic-spike/pom.xml compile exec:java@semantic-fact-benchmark \
      -Dexec.args="$engine $project_root $corpus $options" \
      2> "$debug_output" \
      | tee "$output"

    if [[ "$engine" == "javaparser" && "$project_id" == "micronaut-starter-aws-cdk" ]]; then
      jq -s -e --arg engine "$engine" --argjson expected "$expected_fact_count" '
        length == ($expected + 1)
        and all(.[0:-1][]; .engine == $engine)
        and ( [.[0:-1][] | select(.passed == false) | .factId] == ["RFACT-011"] )
        and all(.[0:-1][] | select(.factId != "RFACT-011"); .passed == true)
        and .[-1].engine == $engine
        and .[-1].status == "completed-with-mismatches"
        and .[-1].facts == $expected
        and .[-1].mismatches == 1
      ' "$output" > /dev/null
      grep -Fq 'call=Function.Builder.create' "$debug_output"
    else
      jq -s -e --arg engine "$engine" --argjson expected "$expected_fact_count" '
        length == ($expected + 1)
        and all(.[0:-1][]; .engine == $engine and .passed == true)
        and .[-1].engine == $engine
        and .[-1].status == "passed"
        and .[-1].facts == $expected
        and .[-1].mismatches == 0
      ' "$output" > /dev/null
    fi
  done

  echo "Representative semantic facts passed: $project_id ($expected_fact_count facts per engine; curated external compile classpath)"
done < <(tail -n +2 "$manifest")

report_count="$(find "$results_directory" -maxdepth 1 -type f -name '*.jsonl' | wc -l | tr -d ' ')"
if [[ "$report_count" -ne 6 ]]; then
  echo "Expected six representative semantic fact reports, found $report_count" >&2
  exit 1
fi
