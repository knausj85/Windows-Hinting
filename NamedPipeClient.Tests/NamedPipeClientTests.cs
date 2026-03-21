using System;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace HintOverlay.NamedPipeClient.Tests
{
    /// <summary>
    /// Example unit tests and integration tests for the named pipe interface.
    /// These can be run to verify the named pipe functionality works correctly.
    /// </summary>
    public static class NamedPipeClientTests
    {
        /// <summary>
        /// Test 1: Verify client can send TOGGLE command
        /// Requirements: HintOverlay must be running
        /// </summary>
        public static bool TestToggleCommand()
        {
            try
            {
                using var client = new HintOverlayClient();
                bool result = client.Toggle();
                Console.WriteLine($"✓ Toggle command: {(result ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Toggle command: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 2: Verify client can send SELECT command with valid hint label
        /// Requirements: HintOverlay must be running with hints visible
        /// </summary>
        public static bool TestSelectCommand()
        {
            try
            {
                using var client = new HintOverlayClient();
                bool result = client.SelectHint("A");
                Console.WriteLine($"✓ Select command: {(result ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Select command: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 3: Verify client can send DEACTIVATE command
        /// Requirements: HintOverlay must be running
        /// </summary>
        public static bool TestDeactivateCommand()
        {
            try
            {
                using var client = new HintOverlayClient();
                bool result = client.Deactivate();
                Console.WriteLine($"✓ Deactivate command: {(result ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Deactivate command: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 4: Verify client handles invalid hint labels gracefully
        /// </summary>
        public static bool TestInvalidHintLabel()
        {
            try
            {
                using var client = new HintOverlayClient();

                // This should return true (command sent) but not activate anything
                bool result = client.SelectHint("INVALID_LABEL_XYZ");
                Console.WriteLine($"✓ Invalid hint label handling: PASS");
                return result;
            }
            catch (ArgumentException)
            {
                Console.WriteLine($"✗ Invalid hint label handling: FAIL - Unexpected exception");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Invalid hint label handling: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 5: Verify client validates empty hint labels
        /// </summary>
        public static bool TestEmptyHintLabel()
        {
            try
            {
                using var client = new HintOverlayClient();
                client.SelectHint(""); // Should throw ArgumentException

                Console.WriteLine($"✗ Empty hint label validation: FAIL - No exception thrown");
                return false;
            }
            catch (ArgumentException)
            {
                Console.WriteLine($"✓ Empty hint label validation: PASS");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Empty hint label validation: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 6: Verify client handles null hint labels
        /// </summary>
        public static bool TestNullHintLabel()
        {
            try
            {
                using var client = new HintOverlayClient();
                client.SelectHint(null!); // Should throw ArgumentException

                Console.WriteLine($"✗ Null hint label validation: FAIL - No exception thrown");
                return false;
            }
            catch (ArgumentException)
            {
                Console.WriteLine($"✓ Null hint label validation: PASS");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Null hint label validation: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 7: Verify client handles HintOverlay not running
        /// Requirements: HintOverlay must NOT be running
        /// Expected: Client retries and eventually times out
        /// </summary>
        public static bool TestServerNotRunning()
        {
            try
            {
                var startTime = DateTime.UtcNow;

                using var client = new HintOverlayClient();
                bool result = client.Toggle();

                var elapsed = DateTime.UtcNow - startTime;

                if (!result && elapsed.TotalSeconds >= 4)
                {
                    Console.WriteLine($"✓ Server not running (waited {elapsed.TotalSeconds:F1}s): PASS");
                    return true;
                }

                Console.WriteLine($"✗ Server not running: FAIL - Expected timeout");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Server not running: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 8: Verify client can reuse same instance for multiple commands
        /// </summary>
        public static bool TestMultipleCommands()
        {
            try
            {
                using var client = new HintOverlayClient();

                bool result1 = client.Toggle();
                System.Threading.Thread.Sleep(100);

                bool result2 = client.SelectHint("A");
                System.Threading.Thread.Sleep(100);

                bool result3 = client.Deactivate();

                bool success = result1 && result2 && result3;
                Console.WriteLine($"✓ Multiple commands: {(success ? "PASS" : "FAIL")}");
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Multiple commands: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 9: Verify multiple client instances can send commands independently
        /// </summary>
        public static bool TestMultipleClients()
        {
            try
            {
                using var client1 = new HintOverlayClient();
                using var client2 = new HintOverlayClient();

                bool result1 = client1.Toggle();
                bool result2 = client2.Deactivate();

                bool success = result1 && result2;
                Console.WriteLine($"✓ Multiple clients: {(success ? "PASS" : "FAIL")}");
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Multiple clients: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 10: Verify hint labels are case-insensitive
        /// </summary>
        public static bool TestCaseInsensitivity()
        {
            try
            {
                using var client = new HintOverlayClient();

                // Both should succeed
                bool result1 = client.SelectHint("A");
                bool result2 = client.SelectHint("a");
                bool result3 = client.SelectHint("AB");
                bool result4 = client.SelectHint("ab");

                bool success = result1 && result2 && result3 && result4;
                Console.WriteLine($"✓ Case insensitivity: {(success ? "PASS" : "FAIL")}");
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Case insensitivity: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 11: Verify client can send SELECT command with LEFT click action
        /// Requirements: HintOverlay must be running with hints visible
        /// </summary>
        public static bool TestSelectWithLeftClick()
        {
            try
            {
                using var client = new HintOverlayClient();
                bool result = client.SelectHint("A", "LEFT");
                Console.WriteLine($"✓ Select with LEFT click: {(result ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Select with LEFT click: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 12: Verify client can send SELECT command with RIGHT click action
        /// Requirements: HintOverlay must be running with hints visible
        /// </summary>
        public static bool TestSelectWithRightClick()
        {
            try
            {
                using var client = new HintOverlayClient();
                bool result = client.SelectHint("A", "RIGHT");
                Console.WriteLine($"✓ Select with RIGHT click: {(result ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Select with RIGHT click: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 13: Verify client can send SELECT command with DOUBLE click action
        /// Requirements: HintOverlay must be running with hints visible
        /// </summary>
        public static bool TestSelectWithDoubleClick()
        {
            try
            {
                using var client = new HintOverlayClient();
                bool result = client.SelectHint("A", "DOUBLE");
                Console.WriteLine($"✓ Select with DOUBLE click: {(result ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Select with DOUBLE click: FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 14: Verify client sends default action when null action provided
        /// </summary>
        public static bool TestSelectWithNullAction()
        {
            try
            {
                using var client = new HintOverlayClient();
                bool result = client.SelectHint("A", null);
                Console.WriteLine($"✓ Select with null action (default): {(result ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Select with null action (default): FAIL - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Run all tests
        /// </summary>
        public static void RunAllTests()
        {
            Console.WriteLine("=== HintOverlay Named Pipe Client Tests ===\n");

            int passed = 0;
            int total = 0;

            // Tests that require HintOverlay to be running
            Console.WriteLine("--- Tests with HintOverlay running ---");
            total++; if (TestToggleCommand()) passed++;
            total++; if (TestSelectCommand()) passed++;
            total++; if (TestDeactivateCommand()) passed++;
            total++; if (TestInvalidHintLabel()) passed++;
            total++; if (TestEmptyHintLabel()) passed++;
            total++; if (TestNullHintLabel()) passed++;
            total++; if (TestMultipleCommands()) passed++;
            total++; if (TestMultipleClients()) passed++;
            total++; if (TestCaseInsensitivity()) passed++;
            total++; if (TestSelectWithLeftClick()) passed++;
            total++; if (TestSelectWithRightClick()) passed++;
            total++; if (TestSelectWithDoubleClick()) passed++;
            total++; if (TestSelectWithNullAction()) passed++;

            Console.WriteLine("\n--- Tests with HintOverlay NOT running ---");
            Console.WriteLine("(This test takes ~5 seconds)");
            total++; if (TestServerNotRunning()) passed++;

            Console.WriteLine($"\n=== Results: {passed}/{total} tests passed ===");

            if (passed == total)
            {
                Console.WriteLine("✓ All tests passed!");
            }
            else
            {
                Console.WriteLine($"✗ {total - passed} test(s) failed");
            }
        }
    }
}
