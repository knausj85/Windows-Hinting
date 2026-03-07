using System;
using System.Collections.Generic;
using System.Linq;
using HintOverlay.Services;
using UIAutomationClient;
using Xunit;

namespace HintOverlay.Tests
{
    public class HintStateManagerTests
    {
        [Fact]
        public void Constructor_ShouldInitializeInInactiveMode()
        {
            // Arrange & Act
            var manager = new HintStateManager();

            // Assert
            Assert.Equal(HintMode.Inactive, manager.CurrentMode);
            Assert.Empty(manager.CurrentHints);
            Assert.Equal("", manager.FilterText);
        }

        [Fact]
        public void Activate_ShouldChangeModeToScanning()
        {
            // Arrange
            var manager = new HintStateManager();
            HintMode? capturedMode = null;
            manager.ModeChanged += (s, mode) => capturedMode = mode;

            // Act
            manager.Activate();

            // Assert
            Assert.Equal(HintMode.Scanning, manager.CurrentMode);
            Assert.Equal(HintMode.Scanning, capturedMode);
        }

        [Fact]
        public void Activate_WhenAlreadyActive_ShouldNotChangeMode()
        {
            // Arrange
            var manager = new HintStateManager();
            manager.Activate();
            var initialMode = manager.CurrentMode;
            int eventCount = 0;
            manager.ModeChanged += (s, mode) => eventCount++;

            // Act
            manager.Activate();

            // Assert
            Assert.Equal(initialMode, manager.CurrentMode);
            Assert.Equal(0, eventCount);
        }

        [Fact]
        public void Deactivate_ShouldClearHintsAndFilter()
        {
            // Arrange
            var manager = new HintStateManager();
            var hints = CreateTestHints(3);
            manager.SetHints(hints);
            manager.AppendToFilter('a');

            // Act
            manager.Deactivate();

            // Assert
            Assert.Equal(HintMode.Inactive, manager.CurrentMode);
            Assert.Empty(manager.CurrentHints);
            Assert.Equal("", manager.FilterText);
        }

        [Fact]
        public void SetHints_ShouldChangeModeToActive()
        {
            // Arrange
            var manager = new HintStateManager();
            manager.Activate();
            var hints = CreateTestHints(3);
            HintMode? capturedMode = null;
            manager.ModeChanged += (s, mode) => capturedMode = mode;

            // Act
            manager.SetHints(hints);

            // Assert
            Assert.Equal(HintMode.Active, manager.CurrentMode);
            Assert.Equal(HintMode.Active, capturedMode);
            Assert.Equal(3, manager.CurrentHints.Count);
        }

        [Fact]
        public void AppendToFilter_ShouldBuildFilterText()
        {
            // Arrange
            var manager = new HintStateManager();
            var hints = CreateTestHints(3);
            manager.SetHints(hints);
            string? capturedFilter = null;
            manager.FilterChanged += (s, filter) => capturedFilter = filter;

            // Act
            manager.AppendToFilter('a');
            manager.AppendToFilter('b');

            // Assert
            Assert.Equal("ab", manager.FilterText);
            Assert.Equal("ab", capturedFilter);
        }

        [Fact]
        public void RemoveLastFilterChar_ShouldRemoveOneCharacter()
        {
            // Arrange
            var manager = new HintStateManager();
            manager.AppendToFilter('a');
            manager.AppendToFilter('b');
            manager.AppendToFilter('c');

            // Act
            manager.RemoveLastFilterChar();

            // Assert
            Assert.Equal("ab", manager.FilterText);
        }

        [Fact]
        public void RemoveLastFilterChar_WhenEmpty_ShouldNotThrow()
        {
            // Arrange
            var manager = new HintStateManager();

            // Act
            var exception = Record.Exception(() => manager.RemoveLastFilterChar());

            // Assert
            Assert.Null(exception);
            Assert.Equal("", manager.FilterText);
        }

        [Fact]
        public void ClearFilter_ShouldResetFilterText()
        {
            // Arrange
            var manager = new HintStateManager();
            manager.AppendToFilter('a');
            manager.AppendToFilter('b');
            string? capturedFilter = null;
            manager.FilterChanged += (s, filter) => capturedFilter = filter;

            // Act
            manager.ClearFilter();

            // Assert
            Assert.Equal("", manager.FilterText);
            Assert.Equal("", capturedFilter);
        }

        [Fact]
        public void GetExactMatch_WithMatchingLabel_ShouldReturnHint()
        {
            // Arrange
            var manager = new HintStateManager();
            var hints = new List<HintItem>
            {
                new HintItem { Label = "AA", Element = null! },
                new HintItem { Label = "AB", Element = null! },
                new HintItem { Label = "AC", Element = null! }
            };
            manager.SetHints(hints);
            manager.AppendToFilter('A');
            manager.AppendToFilter('B');

            // Act
            var match = manager.GetExactMatch();

            // Assert
            Assert.NotNull(match);
            Assert.Equal("AB", match.Label);
        }

        [Fact]
        public void GetExactMatch_CaseInsensitive_ShouldReturnHint()
        {
            // Arrange
            var manager = new HintStateManager();
            var hints = new List<HintItem>
            {
                new HintItem { Label = "AB", Element = null! }
            };
            manager.SetHints(hints);
            manager.AppendToFilter('a');
            manager.AppendToFilter('b');

            // Act
            var match = manager.GetExactMatch();

            // Assert
            Assert.NotNull(match);
            Assert.Equal("AB", match.Label);
        }

        [Fact]
        public void GetExactMatch_WithNoMatch_ShouldReturnNull()
        {
            // Arrange
            var manager = new HintStateManager();
            var hints = CreateTestHints(3);
            manager.SetHints(hints);
            manager.AppendToFilter('Z');
            manager.AppendToFilter('Z');

            // Act
            var match = manager.GetExactMatch();

            // Assert
            Assert.Null(match);
        }

        [Fact]
        public void GetExactMatch_WithEmptyFilter_ShouldReturnNull()
        {
            // Arrange
            var manager = new HintStateManager();
            var hints = CreateTestHints(3);
            manager.SetHints(hints);

            // Act
            var match = manager.GetExactMatch();

            // Assert
            Assert.Null(match);
        }

        [Fact]
        public void HasMatchingHint_WithPartialMatch_ShouldReturnTrue()
        {
            // Arrange
            var manager = new HintStateManager();
            var hints = new List<HintItem>
            {
                new HintItem { Label = "ABC", Element = null! }
            };
            manager.SetHints(hints);

            // Act
            var hasMatch = manager.HasMatchingHint("AB");

            // Assert
            Assert.True(hasMatch);
        }

        [Fact]
        public void HasMatchingHint_WithNoMatch_ShouldReturnFalse()
        {
            // Arrange
            var manager = new HintStateManager();
            var hints = new List<HintItem>
            {
                new HintItem { Label = "ABC", Element = null! }
            };
            manager.SetHints(hints);

            // Act
            var hasMatch = manager.HasMatchingHint("XY");

            // Assert
            Assert.False(hasMatch);
        }

        [Fact]
        public void HintsChanged_ShouldFireWhenHintsAreSet()
        {
            // Arrange
            var manager = new HintStateManager();
            IReadOnlyList<HintItem>? capturedHints = null;
            manager.HintsChanged += (s, hints) => capturedHints = hints;
            var hints = CreateTestHints(2);

            // Act
            manager.SetHints(hints);

            // Assert
            Assert.NotNull(capturedHints);
            Assert.Equal(2, capturedHints.Count);
        }

        private List<HintItem> CreateTestHints(int count)
        {
            var hints = new List<HintItem>();
            for (int i = 0; i < count; i++)
            {
                hints.Add(new HintItem
                {
                    Label = $"H{i}",
                    Element = null!,
                    CurrentOpacity = 1.0f,
                    TargetOpacity = 1.0f
                });
            }
            return hints;
        }
    }
}