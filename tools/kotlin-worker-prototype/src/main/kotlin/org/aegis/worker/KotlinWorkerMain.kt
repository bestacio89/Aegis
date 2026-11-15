package org.aegis.worker

object KotlinWorkerMain {
    @JvmStatic
    fun main(args: Array<String>) {
        KotlinWorkerServer(KotlinSemanticAnalysisEngine()).serve(System.`in`, System.out)
    }
}
