package org.aegis.worker

import org.jetbrains.kotlin.cli.common.ExitCode
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class KotlinSemanticAnalysisEngineTests {
    @Test
    fun `successful compilation is completed`() {
        assertEquals(
            2,
            KotlinSemanticAnalysisEngine().statusForExitCode(ExitCode.OK)
        )
    }

    @Test
    fun `source compilation errors remain partially completed`() {
        assertEquals(
            3,
            KotlinSemanticAnalysisEngine().statusForExitCode(ExitCode.COMPILATION_ERROR)
        )
    }

    @Test
    fun `compiler infrastructure failures are failed`() {
        assertEquals(
            4,
            KotlinSemanticAnalysisEngine().statusForExitCode(ExitCode.INTERNAL_ERROR)
        )
    }
}
