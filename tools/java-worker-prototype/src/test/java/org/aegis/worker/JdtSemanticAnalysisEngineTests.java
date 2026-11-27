package org.aegis.worker;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Comparator;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

final class JdtSemanticAnalysisEngineTests {
    @TempDir
    Path workspace;

    @Test
    void resolves_source_symbols_inheritance_calls_and_annotations() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java/example"));
        Path annotation = sourceRoot.resolve("Component.java");
        Path contract = sourceRoot.resolve("Contract.java");
        Path service = sourceRoot.resolve("Service.java");

        Files.writeString(annotation, "package example; public @interface Component {}");
        Files.writeString(contract, "package example; public interface Contract { void execute(); }");
        Files.writeString(service, """
            package example;
            @Component
            public final class Service implements Contract {
                public void execute() { helper(); }
                private void helper() {}
            }
            """);

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot.getParent()));
        payload.add("sourceFiles", paths(annotation, contract, service));
        payload.add("classpathEntries", new JsonArray());
        payload.addProperty("sourceLevel", "21");
        payload.addProperty("includeTestCode", false);
        payload.addProperty("includeGeneratedCode", false);

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);
        assertEquals(2, result.get("status").getAsInt());
        assertEquals(3, result.get("analyzedFileCount").getAsInt());

        JsonArray symbols = result.getAsJsonArray("symbols");
        assertTrue(symbols.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("qualifiedName").getAsString().equals("example.Service")));
        assertTrue(symbols.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("qualifiedName").getAsString().equals("example.Service#execute()")));

        JsonArray relationships = result.getAsJsonArray("relationships");
        assertTrue(relationships.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 3
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1));
        assertTrue(relationships.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 4
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1));
        assertTrue(relationships.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 7
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1));
        assertTrue(relationships.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 5
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1));
    }

    @Test
    void marks_analysis_partial_when_jdt_reports_coverage_affecting_compiler_errors() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("broken-project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java/example"));
        Path source = sourceRoot.resolve("Broken.java");
        Files.writeString(source, "package example; public class Broken { MissingType value; }");

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot.getParent()));
        payload.add("sourceFiles", paths(source));
        payload.add("classpathEntries", new JsonArray());

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);

        assertEquals(3, result.get("status").getAsInt());
        assertTrue(result.getAsJsonArray("diagnostics").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("severity").getAsString().equals("Error")
                && item.getAsJsonObject().get("affectsCoverage").getAsBoolean()));

        JsonObject sourceType = result.getAsJsonArray("symbols").asList().stream()
            .map(item -> item.getAsJsonObject())
            .filter(item -> item.get("name").getAsString().equals("Broken"))
            .findFirst()
            .orElseThrow();

        assertEquals(1, sourceType.get("origin").getAsInt());
        assertEquals("example", sourceType.get("containerName").getAsString());
        assertEquals("Package", sourceType.get("containerKind").getAsString());
        assertEquals(source.toString(), sourceType.get("filePath").getAsString());
    }

    @Test
    void rejects_source_files_outside_the_declared_module_roots() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path outside = Files.createDirectories(workspace.resolve("outside")).resolve("Leak.java");
        Files.writeString(outside, "class Leak {}");

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot));
        payload.add("sourceFiles", paths(outside));
        payload.add("classpathEntries", new JsonArray());

        assertThrows(IllegalArgumentException.class,
            () -> new JdtSemanticAnalysisEngine().analyze(payload));
    }

    @Test
    void resolves_types_from_only_explicitly_referenced_project_source_roots() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("consumer"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path consumerPackage = Files.createDirectories(sourceRoot.resolve("consumer"));
        Path apiProject = Files.createDirectories(workspace.resolve("api"));
        Path apiSourceRoot = Files.createDirectories(apiProject.resolve("src/main/java"));
        Path apiPackage = Files.createDirectories(apiSourceRoot.resolve("api"));
        Path consumerFile = consumerPackage.resolve("Consumer.java");
        Path apiFile = apiPackage.resolve("PublicApi.java");

        Files.writeString(apiFile, """
            package api;
            public final class PublicApi {
                public String name() { return "api"; }
            }
            """);
        Files.writeString(consumerFile, """
            package consumer;
            import api.PublicApi;
            public final class Consumer {
                public String call(PublicApi api) { return api.name(); }
            }
            """);

        JsonObject unresolvedPayload = new JsonObject();
        unresolvedPayload.addProperty("projectPath", project.toString());
        unresolvedPayload.addProperty("workspaceRoot", workspace.toString());
        unresolvedPayload.add("sourceRoots", paths(sourceRoot));
        unresolvedPayload.add("sourceFiles", paths(consumerFile));
        unresolvedPayload.add("classpathEntries", new JsonArray());

        JsonObject unresolvedResult = new JdtSemanticAnalysisEngine().analyze(unresolvedPayload);
        assertEquals(3, unresolvedResult.get("status").getAsInt());
        assertTrue(unresolvedResult.getAsJsonArray("diagnostics").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("severity").getAsString().equals("Error")
                && item.getAsJsonObject().get("affectsCoverage").getAsBoolean()));

        assertTrue(unresolvedResult.getAsJsonArray("relationships").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 5
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 3));
        assertTrue(unresolvedResult.getAsJsonArray("relationships").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 4
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 3));

        JsonObject resolvedPayload = unresolvedPayload.deepCopy();
        resolvedPayload.add("referencedSourceRoots", paths(apiSourceRoot));
        JsonObject resolvedResult = new JdtSemanticAnalysisEngine().analyze(resolvedPayload);

        assertEquals(2, resolvedResult.get("status").getAsInt());
        assertEquals(1, resolvedResult.get("analyzedFileCount").getAsInt());
        assertTrue(resolvedResult.getAsJsonArray("relationships").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 5
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1
                && item.getAsJsonObject().has("targetSymbolId")
                && !item.getAsJsonObject().get("targetSymbolId").isJsonNull()));
        assertTrue(resolvedResult.getAsJsonArray("relationships").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 4
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1));

        JsonObject apiPayload = new JsonObject();
        apiPayload.addProperty("projectPath", apiProject.toString());
        apiPayload.addProperty("workspaceRoot", workspace.toString());
        apiPayload.add("sourceRoots", paths(apiSourceRoot));
        apiPayload.add("sourceFiles", paths(apiFile));
        apiPayload.add("classpathEntries", new JsonArray());
        JsonObject apiResult = new JdtSemanticAnalysisEngine().analyze(apiPayload);
        String declaredApiSymbolId = apiResult.getAsJsonArray("symbols").asList().stream()
            .map(item -> item.getAsJsonObject())
            .filter(item -> item.get("qualifiedName").getAsString().equals("api.PublicApi"))
            .map(item -> item.get("id").getAsString())
            .findFirst()
            .orElseThrow();

        String resolvedApiSymbolId = resolvedResult.getAsJsonArray("relationships").asList().stream()
            .map(item -> item.getAsJsonObject())
            .filter(item -> item.get("kind").getAsInt() == 5
                && item.get("resolutionStatus").getAsInt() == 1
                && item.get("targetSymbolId").getAsString().equals(declaredApiSymbolId))
            .map(item -> item.get("targetSymbolId").getAsString())
            .findFirst()
            .orElseThrow();

        assertEquals(declaredApiSymbolId, resolvedApiSymbolId);
        assertFalse(resolvedResult.getAsJsonArray("symbols").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("qualifiedName").getAsString().equals("api.PublicApi")));
    }

    @Test
    void rejects_referenced_source_roots_outside_the_workspace_boundary() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("consumer"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path source = sourceRoot.resolve("Consumer.java");
        Files.writeString(source, "package consumer; public final class Consumer {}");

        Path outsideWorkspace = Files.createDirectories(
            workspace.getParent().resolve("aegis-outside-" + java.util.UUID.randomUUID()));
        try {
            Path outsideSourceRoot = Files.createDirectories(outsideWorkspace.resolve("src/main/java"));
            JsonObject payload = new JsonObject();
            payload.addProperty("projectPath", project.toString());
            payload.addProperty("workspaceRoot", workspace.toString());
            payload.add("sourceRoots", paths(sourceRoot));
            payload.add("referencedSourceRoots", paths(outsideSourceRoot));
            payload.add("sourceFiles", paths(source));
            payload.add("classpathEntries", new JsonArray());

            assertThrows(IllegalArgumentException.class,
                () -> new JdtSemanticAnalysisEngine().analyze(payload));
        } finally {
            deleteRecursively(outsideWorkspace);
        }
    }

    @Test
    void does_not_classify_production_sources_as_tests_because_of_ancestor_or_package_names() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("tests").resolve("project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java/com/test"));
        Path source = sourceRoot.resolve("Service.java");
        Files.writeString(source, "package com.test; public final class Service {}");

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(project.resolve("src/main/java")));
        payload.add("sourceFiles", paths(source));
        payload.add("classpathEntries", new JsonArray());
        payload.addProperty("includeTestCode", false);
        payload.addProperty("includeGeneratedCode", false);

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);

        assertEquals(2, result.get("status").getAsInt());
        assertEquals(1, result.get("analyzedFileCount").getAsInt());
        assertEquals(0, result.get("skippedFileCount").getAsInt());
        assertTrue(result.getAsJsonArray("symbols").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("qualifiedName").getAsString().equals("com.test.Service")));
    }

    @Test
    void does_not_classify_production_sources_as_generated_because_of_ancestor_or_package_names() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("generated").resolve("project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java/com/generated"));
        Path source = sourceRoot.resolve("Service.java");
        Files.writeString(source, "package com.generated; public final class Service {}");

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(project.resolve("src/main/java")));
        payload.add("sourceFiles", paths(source));
        payload.add("classpathEntries", new JsonArray());
        payload.addProperty("includeTestCode", false);
        payload.addProperty("includeGeneratedCode", false);

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);

        assertEquals(2, result.get("status").getAsInt());
        assertEquals(1, result.get("analyzedFileCount").getAsInt());
        assertEquals(0, result.get("skippedFileCount").getAsInt());
        assertTrue(result.getAsJsonArray("symbols").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("qualifiedName").getAsString().equals("com.generated.Service")));
    }


    @Test
    void discovers_spring_controller_routes_from_resolved_annotation_bindings() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("spring-project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path annotationRoot = Files.createDirectories(sourceRoot.resolve("org/springframework/web/bind/annotation"));
        Path controllerRoot = Files.createDirectories(sourceRoot.resolve("example"));
        Files.writeString(annotationRoot.resolve("RequestMethod.java"), """
            package org.springframework.web.bind.annotation;
            public enum RequestMethod { GET, POST, PUT, DELETE, PATCH }
            """);
        Files.writeString(annotationRoot.resolve("RequestMapping.java"), """
            package org.springframework.web.bind.annotation;
            import java.lang.annotation.*;
            @Target({ElementType.TYPE, ElementType.METHOD})
            public @interface RequestMapping {
                String[] value() default {};
                String[] path() default {};
                RequestMethod[] method() default {};
            }
            """);
        Files.writeString(annotationRoot.resolve("GetMapping.java"), """
            package org.springframework.web.bind.annotation;
            import java.lang.annotation.*;
            @Target(ElementType.METHOD)
            public @interface GetMapping {
                String[] value() default {};
                String[] path() default {};
            }
            """);
        Files.writeString(controllerRoot.resolve("OrdersController.java"), """
            package example;
            import org.springframework.web.bind.annotation.*;
            @RequestMapping("/api/orders")
            public final class OrdersController {
                @GetMapping("/{id}")
                public String getOrder() { return "ok"; }

                @RequestMapping(path = "/search", method = {RequestMethod.GET, RequestMethod.POST})
                public String search() { return "ok"; }

                static final String CONSTANT_ROUTE = "/constant";
                @GetMapping(CONSTANT_ROUTE)
                public String constant() { return "ok"; }

                @GetMapping("${orders.route}")
                public String dynamic() { return "ok"; }
            }
            """);

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot));
        payload.add("sourceFiles", paths(
            annotationRoot.resolve("RequestMethod.java"),
            annotationRoot.resolve("RequestMapping.java"),
            annotationRoot.resolve("GetMapping.java"),
            controllerRoot.resolve("OrdersController.java")));
        payload.add("classpathEntries", new JsonArray());

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);
        JsonArray endpoints = result.getAsJsonArray("serverApiEndpoints");
        assertEquals(4, endpoints.size());

        JsonObject get = endpoints.asList().stream().map(item -> item.getAsJsonObject())
            .filter(item -> item.get("handlerIdentity").getAsString().contains("getOrder"))
            .findFirst().orElseThrow();
        assertEquals("/api/orders/{id}", get.get("routeTemplate").getAsString());
        assertEquals(List.of("GET"), get.getAsJsonArray("httpMethods").asList().stream()
            .map(item -> item.getAsString()).toList());
        assertEquals(1, get.get("resolutionStatus").getAsInt());
        assertEquals(2, get.getAsJsonArray("routeCompositionEvidence").size());

        JsonObject search = endpoints.asList().stream().map(item -> item.getAsJsonObject())
            .filter(item -> item.get("handlerIdentity").getAsString().contains("search"))
            .findFirst().orElseThrow();
        assertEquals("/api/orders/search", search.get("routeTemplate").getAsString());
        assertEquals(List.of("GET", "POST"), search.getAsJsonArray("httpMethods").asList().stream()
            .map(item -> item.getAsString()).sorted().toList());

        JsonObject dynamic = endpoints.asList().stream().map(item -> item.getAsJsonObject())
            .filter(item -> item.get("handlerIdentity").getAsString().contains("constant"))
            .findFirst().orElseThrow();
        assertEquals("/api/orders/constant", dynamic.get("routeTemplate").getAsString());
        assertEquals(1, dynamic.get("resolutionStatus").getAsInt());

        JsonObject unresolved = endpoints.asList().stream().map(item -> item.getAsJsonObject())
            .filter(item -> item.get("handlerIdentity").getAsString().contains("dynamic"))
            .findFirst().orElseThrow();
        assertEquals("/api/orders/${orders.route}", unresolved.get("routeTemplate").getAsString());
        assertEquals(3, unresolved.get("resolutionStatus").getAsInt());
    }


    @Test
    void discovers_spring_rest_template_outbound_requests_with_binding_provenance() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("client-project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path restTemplatePackage = Files.createDirectories(sourceRoot.resolve("org/springframework/web/client"));
        Path httpPackage = Files.createDirectories(sourceRoot.resolve("org/springframework/http"));
        Path webClientPackage = Files.createDirectories(sourceRoot.resolve("org/springframework/web/reactive/function/client"));
        Path appPackage = Files.createDirectories(sourceRoot.resolve("example"));

        Path restTemplate = restTemplatePackage.resolve("RestTemplate.java");
        Path httpMethod = httpPackage.resolve("HttpMethod.java");
        Path webClient = webClientPackage.resolve("WebClient.java");
        Path client = appPackage.resolve("OrderGateway.java");
        Files.writeString(restTemplate, """
            package org.springframework.web.client;
            public class RestTemplate {
                public <T> T getForObject(String url, Class<T> responseType) { return null; }
                public <T> T postForObject(String url, Object request, Class<T> responseType) { return null; }
                public <T> T exchange(String url, org.springframework.http.HttpMethod method, Object request, Class<T> responseType) { return null; }
                public void delete(String url) {}
            }
            """);
        Files.writeString(webClient, """
            package org.springframework.web.reactive.function.client;
            public class WebClient {
                public UriSpec get() { return null; }
                public UriSpec post() { return null; }
                public static class UriSpec {
                    public UriSpec uri(String value) { return this; }
                }
            }
            """);
        Files.writeString(httpMethod, """
            package org.springframework.http;
            public enum HttpMethod { GET, POST, PUT, PATCH, DELETE }
            """);
        Files.writeString(client, """
            package example;
            import org.springframework.web.client.RestTemplate;
            import org.springframework.http.HttpMethod;
            import org.springframework.web.reactive.function.client.WebClient;
            public class OrderGateway {
                private final RestTemplate client = new RestTemplate();
                private final WebClient webClient = new WebClient();
                public static class UnrelatedClient {
                    public Object getForObject(String url) { return null; }
                }
                public void load(String orderId) {
                    client.getForObject("/orders", Object.class);
                    client.postForObject("/orders", new Object(), Object.class);
                    client.exchange("/orders/" + orderId, HttpMethod.GET, null, Object.class);
                    client.delete("/orders/42");
                    webClient.get().uri("/catalog/items");
                    new UnrelatedClient().getForObject("/not-an-api-client");
                }
            }
            """);

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot));
        payload.add("sourceFiles", paths(restTemplate, webClient, httpMethod, client));
        payload.add("classpathEntries", new JsonArray());

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);
        JsonArray requests = result.getAsJsonArray("apiClientRequests");
        assertEquals(5, requests.size());

        JsonObject get = requests.asList().stream().map(item -> item.getAsJsonObject())
            .filter(item -> item.get("declarationEvidence").getAsString().contains("getForObject"))
            .findFirst().orElseThrow();
        assertEquals("GET", get.get("httpMethod").getAsString());
        assertEquals("\"/orders\"", get.get("requestUriExpression").getAsString());
        assertEquals(0, get.get("resolutionStatus").getAsInt());
        assertEquals("Java", get.get("language").getAsString());
        assertTrue(get.get("callerIdentity").getAsString().contains("OrderGateway#load"));


        JsonObject webClientRequest = requests.asList().stream().map(item -> item.getAsJsonObject())
            .filter(item -> item.get("framework").getAsString().equals("Spring WebClient"))
            .findFirst().orElseThrow();
        assertEquals("GET", webClientRequest.get("httpMethod").getAsString());
        assertEquals("\"/catalog/items\"", webClientRequest.get("requestUriExpression").getAsString());
        assertEquals(0, webClientRequest.get("resolutionStatus").getAsInt());

        JsonObject dynamic = requests.asList().stream().map(item -> item.getAsJsonObject())
            .filter(item -> item.get("declarationEvidence").getAsString().contains("exchange"))
            .findFirst().orElseThrow();
        assertEquals("GET", dynamic.get("httpMethod").getAsString());
        assertEquals(2, dynamic.get("resolutionStatus").getAsInt());
        assertTrue(dynamic.get("requestUriExpression").getAsString().contains("orderId"));
    }


    @Test
    void discovers_spring_kafka_listeners_and_kafka_template_sends_from_resolved_bindings() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("spring-kafka-project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path kafkaAnnotationRoot = Files.createDirectories(sourceRoot.resolve("org/springframework/kafka/annotation"));
        Path kafkaCoreRoot = Files.createDirectories(sourceRoot.resolve("org/springframework/kafka/core"));
        Path kafkaModelRoot = Files.createDirectories(sourceRoot.resolve("org/apache/kafka/clients/consumer"));
        Path appRoot = Files.createDirectories(sourceRoot.resolve("example"));
        Files.writeString(kafkaAnnotationRoot.resolve("KafkaListener.java"), """
            package org.springframework.kafka.annotation;
            import java.lang.annotation.*;
            @Target(ElementType.METHOD)
            public @interface KafkaListener {
                String[] topics() default {};
                String topicPattern() default "";
                TopicPartition[] topicPartitions() default {};
            }
            """);
        Files.writeString(kafkaAnnotationRoot.resolve("TopicPartition.java"), """
            package org.springframework.kafka.annotation;
            import java.lang.annotation.*;
            @Target({})
            public @interface TopicPartition {
                String topic();
                String[] partitions() default {};
            }
            """);
        Files.writeString(kafkaCoreRoot.resolve("KafkaTemplate.java"), """
            package org.springframework.kafka.core;
            public class KafkaTemplate<K, V> {
                public Object send(String topic, V data) { return null; }
                public Object send(String topic, K key, V data) { return null; }
            }
            """);
        Files.writeString(kafkaModelRoot.resolve("ConsumerRecord.java"), """
            package org.apache.kafka.clients.consumer;
            public class ConsumerRecord<K, V> {}
            """);
        Path unrelatedAnnotation = appRoot.resolve("KafkaListener.java");
        Files.writeString(unrelatedAnnotation, """
            package example;
            import java.lang.annotation.*;
            @Target(ElementType.METHOD)
            public @interface KafkaListener { String[] topics() default {}; }
            """);
        Path listener = appRoot.resolve("OrderListener.java");
        Files.writeString(listener, """
            package example;
            import org.springframework.kafka.annotation.KafkaListener;
            import org.apache.kafka.clients.consumer.ConsumerRecord;
            public final class OrderListener {
                @KafkaListener(
                    topics = {"orders", "${orders.topic}"},
                    topicPattern = "#{@topicPattern}",
                    topicPartitions = {@org.springframework.kafka.annotation.TopicPartition(topic = "priority-orders", partitions = {"0"})})
                public void consume(ConsumerRecord<String, String> record) {}
                @example.KafkaListener(topics = {"not-kafka"})
                public void unrelated(String value) {}
            }
            """);
        Path sender = appRoot.resolve("OrderSender.java");
        Files.writeString(sender, """
            package example;
            import org.springframework.kafka.core.KafkaTemplate;
            public final class OrderSender {
                private final KafkaTemplate<String, String> kafka = new KafkaTemplate<>();
                public void send(String topic, String payload) {
                    kafka.send("orders", payload);
                    kafka.send(topic, "order-42", payload);
                    new UnrelatedSender().send("not-kafka", payload);
                }
                static final class UnrelatedSender {
                    Object send(String topic, String payload) { return null; }
                }
            }
            """);

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot));
        payload.add("sourceFiles", paths(
            kafkaAnnotationRoot.resolve("KafkaListener.java"),
            kafkaAnnotationRoot.resolve("TopicPartition.java"),
            kafkaCoreRoot.resolve("KafkaTemplate.java"),
            kafkaModelRoot.resolve("ConsumerRecord.java"),
            unrelatedAnnotation,
            listener,
            sender));
        payload.add("classpathEntries", new JsonArray());

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);
        JsonArray integrations = result.getAsJsonArray("messageIntegrations");
        assertEquals(6, integrations.size());

        List<JsonObject> values = integrations.asList().stream().map(item -> item.getAsJsonObject()).toList();
        List<JsonObject> consumes = values.stream()
            .filter(item -> item.get("operation").getAsInt() == 1).toList();
        List<JsonObject> sends = values.stream()
            .filter(item -> item.get("operation").getAsInt() == 2).toList();
        assertEquals(4, consumes.size());
        assertEquals(2, sends.size());

        JsonObject literalTopic = consumes.stream()
            .filter(item -> item.has("destinationIdentity")
                && item.get("destinationIdentity").getAsString().equals("orders"))
            .findFirst().orElseThrow();
        assertEquals(0, literalTopic.get("resolutionStatus").getAsInt());
        assertEquals("java.lang.String", literalTopic.get("messageTypeIdentity").getAsString());
        assertTrue(literalTopic.get("handlerIdentity").getAsString().contains("OrderListener#consume"));

        JsonObject dynamicTopic = consumes.stream()
            .filter(item -> item.get("destinationExpression").getAsString().contains("orders.topic"))
            .findFirst().orElseThrow();
        assertEquals(2, dynamicTopic.get("resolutionStatus").getAsInt());
        assertFalse(dynamicTopic.has("destinationIdentity"));

        JsonObject pattern = consumes.stream()
            .filter(item -> item.get("destinationExpression").getAsString().contains("topicPattern"))
            .findFirst().orElseThrow();
        assertEquals(2, pattern.get("resolutionStatus").getAsInt());

        JsonObject literalSend = sends.stream()
            .filter(item -> item.has("destinationIdentity"))
            .findFirst().orElseThrow();
        assertEquals("orders", literalSend.get("destinationIdentity").getAsString());
        assertEquals("java.lang.String", literalSend.get("messageTypeIdentity").getAsString());
        assertEquals(0, literalSend.get("resolutionStatus").getAsInt());

        JsonObject dynamicSend = sends.stream()
            .filter(item -> !item.has("destinationIdentity"))
            .findFirst().orElseThrow();
        assertEquals(2, dynamicSend.get("resolutionStatus").getAsInt());
        assertTrue(dynamicSend.get("destinationExpression").getAsString().contains("topic"));
        assertTrue(values.stream().allMatch(item ->
            item.get("framework").getAsString().equals("Spring Kafka")
                && item.get("providerId").getAsString().equals("eclipse-jdt-spring-kafka")
                && item.get("destinationKind").getAsInt() == 1));
    }


    @Test
    void discovers_spring_jms_listeners_and_template_sends_from_resolved_bindings() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("spring-jms-project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path annotationRoot = Files.createDirectories(sourceRoot.resolve("org/springframework/jms/annotation"));
        Path coreRoot = Files.createDirectories(sourceRoot.resolve("org/springframework/jms/core"));
        Path appRoot = Files.createDirectories(sourceRoot.resolve("example"));
        Files.writeString(annotationRoot.resolve("JmsListener.java"), """
            package org.springframework.jms.annotation;
            import java.lang.annotation.*;
            @Target(ElementType.METHOD)
            public @interface JmsListener {
                String destination() default "";
            }
            """);
        Files.writeString(coreRoot.resolve("MessagePostProcessor.java"), """
            package org.springframework.jms.core;
            public interface MessagePostProcessor {}
            """);
        Files.writeString(coreRoot.resolve("JmsTemplate.java"), """
            package org.springframework.jms.core;
            public class JmsTemplate {
                public void convertAndSend(String destinationName, Object message) {}
                public void convertAndSend(String destinationName, Object message, MessagePostProcessor processor) {}
            }
            """);
        Path unrelatedAnnotation = appRoot.resolve("JmsListener.java");
        Files.writeString(unrelatedAnnotation, """
            package example;
            import java.lang.annotation.*;
            @Target(ElementType.METHOD)
            public @interface JmsListener { String destination() default ""; }
            """);
        Path listener = appRoot.resolve("OrderJmsListener.java");
        Files.writeString(listener, """
            package example;
            import org.springframework.jms.annotation.JmsListener;
            public final class OrderJmsListener {
                @JmsListener(destination = "orders.queue")
                public void consume(String payload) {}
                @JmsListener(destination = "${orders.destination}")
                public void consumeDynamic(String payload) {}
                @example.JmsListener(destination = "not-jms")
                public void unrelated(String payload) {}
            }
            """);
        Path sender = appRoot.resolve("OrderJmsSender.java");
        Files.writeString(sender, """
            package example;
            import org.springframework.jms.core.JmsTemplate;
            import org.springframework.jms.core.MessagePostProcessor;
            public final class OrderJmsSender {
                private final JmsTemplate jms = new JmsTemplate();
                private final MessagePostProcessor processor = null;
                public void send(String destination, String payload) {
                    jms.convertAndSend("orders.queue", payload);
                    jms.convertAndSend(destination, payload, processor);
                    new UnrelatedSender().convertAndSend("not-jms", payload);
                }
                static final class UnrelatedSender {
                    void convertAndSend(String destination, Object message) {}
                }
            }
            """);

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot));
        payload.add("sourceFiles", paths(
            annotationRoot.resolve("JmsListener.java"),
            coreRoot.resolve("MessagePostProcessor.java"),
            coreRoot.resolve("JmsTemplate.java"),
            unrelatedAnnotation,
            listener,
            sender));
        payload.add("classpathEntries", new JsonArray());

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);
        JsonArray integrations = result.getAsJsonArray("messageIntegrations");
        assertEquals(4, integrations.size());

        List<JsonObject> values = integrations.asList().stream().map(item -> item.getAsJsonObject()).toList();
        List<JsonObject> consumes = values.stream()
            .filter(item -> item.get("operation").getAsInt() == 1).toList();
        List<JsonObject> sends = values.stream()
            .filter(item -> item.get("operation").getAsInt() == 2).toList();
        assertEquals(2, consumes.size());
        assertEquals(2, sends.size());

        JsonObject literalListener = consumes.stream()
            .filter(item -> item.has("destinationIdentity")).findFirst().orElseThrow();
        assertEquals("orders.queue", literalListener.get("destinationIdentity").getAsString());
        assertEquals("java.lang.String", literalListener.get("messageTypeIdentity").getAsString());
        assertEquals(0, literalListener.get("resolutionStatus").getAsInt());
        assertEquals(0, literalListener.get("destinationKind").getAsInt());

        JsonObject dynamicListener = consumes.stream()
            .filter(item -> !item.has("destinationIdentity")).findFirst().orElseThrow();
        assertEquals(2, dynamicListener.get("resolutionStatus").getAsInt());

        JsonObject literalSend = sends.stream()
            .filter(item -> item.has("destinationIdentity")).findFirst().orElseThrow();
        assertEquals("orders.queue", literalSend.get("destinationIdentity").getAsString());
        assertEquals("java.lang.String", literalSend.get("messageTypeIdentity").getAsString());
        assertEquals(0, literalSend.get("resolutionStatus").getAsInt());
        assertEquals(0, literalSend.get("destinationKind").getAsInt());

        JsonObject dynamicSend = sends.stream()
            .filter(item -> !item.has("destinationIdentity")).findFirst().orElseThrow();
        assertEquals(2, dynamicSend.get("resolutionStatus").getAsInt());
        assertTrue(values.stream().allMatch(item ->
            item.get("framework").getAsString().equals("Spring JMS")
                && item.get("providerId").getAsString().equals("eclipse-jdt-spring-jms")));
    }

    @Test
    void emits_package_declaration_symbols_for_package_info_and_default_package() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("package-project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path nestedPackage = Files.createDirectories(sourceRoot.resolve("com/acme/internal"));
        Path packageInfo = nestedPackage.resolve("package-info.java");
        Path defaultPackageSource = sourceRoot.resolve("DefaultType.java");
        Files.writeString(packageInfo, "package com.acme.internal;");
        Files.writeString(defaultPackageSource, "final class DefaultType {}");

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot));
        payload.add("sourceFiles", paths(packageInfo, defaultPackageSource));
        payload.add("classpathEntries", new JsonArray());

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);
        List<JsonObject> packageSymbols = result.getAsJsonArray("symbols").asList().stream()
            .map(item -> item.getAsJsonObject())
            .filter(item -> item.get("kind").getAsInt() == 1)
            .toList();

        assertEquals(2, packageSymbols.size());
        JsonObject declaredPackage = packageSymbols.stream()
            .filter(item -> item.get("qualifiedName").getAsString().equals("com.acme.internal"))
            .findFirst()
            .orElseThrow();
        assertEquals("com.acme.internal", declaredPackage.get("name").getAsString());
        assertEquals("Package", declaredPackage.get("containerKind").getAsString());
        assertEquals("com.acme.internal", declaredPackage.get("containerName").getAsString());
        assertEquals(packageInfo.toString(), declaredPackage.get("filePath").getAsString());

        JsonObject defaultPackage = packageSymbols.stream()
            .filter(item -> item.get("qualifiedName").getAsString().equals("<default>"))
            .findFirst()
            .orElseThrow();
        assertEquals("<default>", defaultPackage.get("name").getAsString());
        assertEquals(defaultPackage.get("filePath").getAsString(), defaultPackage.get("provenance")
            .getAsJsonArray().get(0).getAsJsonObject().get("filePath").getAsString());
        assertEquals(0, result.getAsJsonArray("relationships").size());
    }

    @Test
    void rejects_source_files_over_the_per_file_budget() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("oversized-project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path source = sourceRoot.resolve("Oversized.java");
        try (var output = Files.newOutputStream(source)) {
            output.write(new byte[16 * 1024 * 1024 + 1]);
        }
        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot));
        payload.add("sourceFiles", paths(source));
        payload.add("classpathEntries", new JsonArray());
        IllegalArgumentException exception = assertThrows(IllegalArgumentException.class,
            () -> new JdtSemanticAnalysisEngine().analyze(payload));
        assertTrue(exception.getMessage().contains("16 MiB per-file limit"));
    }

    @Test
    void rejects_source_manifests_over_the_16_mib_boundary() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("manifest-project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path manifest = workspace.resolve("oversized-source-manifest.json");
        try (var output = Files.newOutputStream(manifest)) {
            output.write('[');
            output.write(new byte[16 * 1024 * 1024]);
        }
        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot));
        payload.addProperty("sourceManifestPath", manifest.toString());
        payload.add("classpathEntries", new JsonArray());
        IllegalArgumentException exception = assertThrows(IllegalArgumentException.class,
            () -> new JdtSemanticAnalysisEngine().analyze(payload));
        assertTrue(exception.getMessage().contains("16 MiB"));
    }

    @Test
    void rejects_source_manifests_with_more_than_50000_entries() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("manifest-count-project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path manifest = workspace.resolve("too-many-sources.json");
        String entries = java.util.stream.IntStream.range(0, 50_001)
            .mapToObj(index -> "\"missing-" + index + ".java\"")
            .collect(java.util.stream.Collectors.joining(",", "[", "]"));
        Files.writeString(manifest, entries);
        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot));
        payload.addProperty("sourceManifestPath", manifest.toString());
        payload.add("classpathEntries", new JsonArray());
        IllegalArgumentException exception = assertThrows(IllegalArgumentException.class,
            () -> new JdtSemanticAnalysisEngine().analyze(payload));
        assertTrue(exception.getMessage().contains("50,000-file limit"));
    }

    private static void deleteRecursively(Path directory) throws IOException {
        try (var paths = Files.walk(directory)) {
            paths.sorted(Comparator.reverseOrder()).forEach(path -> {
                try { Files.deleteIfExists(path); }
                catch (IOException exception) {
                    throw new java.io.UncheckedIOException(exception);
                }
            });
        }
    }

    private static JsonArray paths(Path... values) {
        JsonArray array = new JsonArray();
        for (Path value : values) array.add(value.toString());
        return array;
    }
}
