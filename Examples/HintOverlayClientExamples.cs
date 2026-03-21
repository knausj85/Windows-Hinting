using System;
using HintOverlay.NamedPipeClient;

namespace HintOverlay.Examples
{
    /// <summary>
    /// Example usage of the HintOverlayClient for controlling Windows-Hinting from external applications.
    /// </summary>
    internal static class HintOverlayClientExamples
    {
        /// <summary>
        /// Basic example: Toggle hints on/off
        /// </summary>
        public static void BasicToggleExample()
        {
            using var client = new HintOverlayClient();

            if (client.Toggle())
            {
                Console.WriteLine("Successfully toggled Windows-Hinting");
            }
            else
            {
                Console.WriteLine("Failed to connect to Windows-Hinting");
            }
        }

        /// <summary>
        /// Example: Select a specific hint and activate it
        /// </summary>
        public static void SelectHintExample()
        {
            using var client = new HintOverlayClient();

            // First, ensure hints are active
            client.Toggle();

            // Wait a moment for hints to load
            System.Threading.Thread.Sleep(500);

            // Select and activate hint "A"
            if (client.SelectHint("A"))
            {
                Console.WriteLine("Successfully selected hint A");
            }
            else
            {
                Console.WriteLine("Failed to select hint A");
            }
        }

        /// <summary>
        /// Example: Sequential hint selection (simulating user typing)
        /// </summary>
        public static void SequentialSelectionExample()
        {
            using var client = new HintOverlayClient();

            // Activate hints
            client.Toggle();
            System.Threading.Thread.Sleep(500);

            // Select hint with label "AB"
            if (client.SelectHint("AB"))
            {
                Console.WriteLine("Selected hint AB");
            }
        }

        /// <summary>
        /// Example: Toggle with automatic retry handling
        /// The client automatically handles connection retries
        /// </summary>
        public static void AutomaticRetryExample()
        {
            using var client = new HintOverlayClient();

            // This will work even if Windows-Hinting is starting up
            // The client will retry for up to 5 seconds automatically
            bool success = client.Toggle();

            if (success)
            {
                Console.WriteLine("Command sent (Windows-Hinting may have been starting)");
            }
            else
            {
                Console.WriteLine("Failed after 5 seconds of retrying");
            }
        }

        /// <summary>
        /// Example: Deactivate hints
        /// </summary>
        public static void DeactivateExample()
        {
            using var client = new HintOverlayClient();

            if (client.Deactivate())
            {
                Console.WriteLine("Successfully deactivated Windows-Hinting");
            }
        }

        /// <summary>
        /// Example: Error handling
        /// </summary>
        public static void ErrorHandlingExample()
        {
            using var client = new HintOverlayClient();

            try
            {
                if (!client.SelectHint("XYZ"))
                {
                    Console.WriteLine("Failed to send command - Windows-Hinting not responding?");
                }
                else
                {
                    Console.WriteLine("Command sent - check Windows-Hinting logs for result");
                }
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid argument: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Integration with keyboard shortcuts
        /// Could be used by a global hotkey handler to control Windows-Hinting
        /// </summary>
        public static void KeyboardIntegrationExample()
        {
            using var client = new HintOverlayClient();

            // Hypothetical: You have a global hotkey handler that calls this
            // when user presses Ctrl+Shift+H to toggle hints
            var success = client.Toggle();

            // You could log the result
            if (!success)
            {
                Console.WriteLine("Warning: Unable to toggle Windows-Hinting");
            }
        }

        /// <summary>
        /// Example: UI Control Integration
        /// You could have a UI element that controls Windows-Hinting
        /// </summary>
        public static void UIControlExample()
        {
            using var client = new HintOverlayClient();

            // Example of button click handler
            // When user clicks "Toggle Hints" button
            Console.WriteLine("Toggle Hints button clicked");

            if (client.Toggle())
            {
                // Update UI to show success
                Console.WriteLine("Hints toggled successfully");
            }
            else
            {
                // Show error to user
                Console.WriteLine("Could not reach Windows-Hinting service");
            }
        }

        /// <summary>
        /// Example: Automated testing or scripting
        /// </summary>
        public static void AutomationExample()
        {
            using var client = new HintOverlayClient();

            // Test 1: Toggle ON
            client.Toggle();
            System.Threading.Thread.Sleep(1000);

            // Test 2: Select hint
            client.SelectHint("A");
            System.Threading.Thread.Sleep(1000);

            // Test 3: Toggle OFF
            client.Deactivate();

            Console.WriteLine("Automation test completed");
        }

        /// <summary>
        /// Example: Select a hint with a specific click action
        /// Actions: "LEFT" (left click), "RIGHT" (right click), "DOUBLE" (double click), or null for default activation
        /// </summary>
        public static void ClickActionExample()
        {
            using var client = new HintOverlayClient();

            // First, ensure hints are active
            client.Toggle();
            System.Threading.Thread.Sleep(500);

            // Default activation (uses UI Automation invoke pattern)
            client.SelectHint("A");

            // Or equivalently
            client.SelectHint("A", null);

            // Left click the element
            client.Toggle();
            System.Threading.Thread.Sleep(500);
            client.SelectHint("A", "LEFT");

            // Right click the element (e.g., to open context menu)
            client.Toggle();
            System.Threading.Thread.Sleep(500);
            client.SelectHint("B", "RIGHT");

            // Double click the element
            client.Toggle();
            System.Threading.Thread.Sleep(500);
            client.SelectHint("C", "DOUBLE");
        }
    }
}
