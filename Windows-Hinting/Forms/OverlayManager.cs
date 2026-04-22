using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting.Forms
{
    /// <summary>
    /// Manages a collection of OverlayForm instances, one per Screen.
    /// Rebuilds the collection when display topology changes.
    /// </summary>
    internal sealed class OverlayManager : IDisposable
    {
        private readonly ILogger _logger;
        private List<OverlayForm> _overlays = new();
        private bool _disposed;

        public bool ShowRectangles { get; set; }
        public HintPosition HintPosition { get; set; } = HintPosition.UpperLeft;

        public OverlayManager(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            RebuildOverlays();
        }

        public void RebuildOverlays()
        {
            _logger.Info("Rebuilding overlays for current screen configuration");

            // Dispose existing overlays
            foreach (var overlay in _overlays)
            {
                try
                {
                    overlay.Hide();
                    overlay.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error disposing overlay: {ex.Message}");
                }
            }
            _overlays.Clear();

            // Create one overlay per screen
            var screens = Screen.AllScreens;
            _logger.Info($"Creating {screens.Length} overlay(s) for {screens.Length} screen(s)");

            foreach (var screen in screens)
            {
                try
                {
                    var overlay = new OverlayForm(screen, _logger)
                    {
                        ShowRectangles = this.ShowRectangles,
                        HintPosition = this.HintPosition
                    };
                    overlay.Show();
                    _overlays.Add(overlay);

                    _logger.Debug($"Created overlay for screen: {screen.Bounds}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to create overlay for screen {screen.Bounds}", ex);
                }
            }
        }

        public void SetEnabled(bool enabled)
        {
            foreach (var overlay in _overlays)
            {
                overlay.SetEnabled(enabled);
            }
        }

        public void SetActiveState(bool active)
        {
            foreach (var overlay in _overlays)
            {
                overlay.SetActiveState(active);
            }
        }

        public void SetHints(List<HintItem> hints)
        {
            // Each overlay filters hints to its own screen
            foreach (var overlay in _overlays)
            {
                overlay.SetHints(hints);
            }
        }

        public void SetFilterPrefix(string prefix)
        {
            foreach (var overlay in _overlays)
            {
                overlay.SetFilterPrefix(prefix);
            }
        }

        public void EnsureTopmost()
        {
            foreach (var overlay in _overlays)
            {
                overlay.EnsureTopmost();
            }
        }

        /// <summary>
        /// Posts an action to the UI thread via one of the overlay forms' message queue.
        /// Returns false if no overlay is available to marshal through.
        /// </summary>
        public bool BeginInvoke(Action action)
        {
            foreach (var overlay in _overlays)
            {
                if (overlay.IsHandleCreated)
                {
                    overlay.BeginInvoke(action);
                    return true;
                }
            }
            return false;
        }

        public bool IsHandleCreated
        {
            get
            {
                foreach (var overlay in _overlays)
                {
                    if (overlay.IsHandleCreated)
                        return true;
                }
                return false;
            }
        }

        public void ApplyShowRectangles(bool show)
        {
            ShowRectangles = show;
            foreach (var overlay in _overlays)
            {
                overlay.ShowRectangles = show;
                overlay.Invalidate();
            }
        }

        public void ApplyHintPosition(HintPosition position)
        {
            HintPosition = position;
            foreach (var overlay in _overlays)
            {
                overlay.HintPosition = position;
                overlay.Invalidate();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (var overlay in _overlays)
            {
                try
                {
                    overlay.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error disposing overlay: {ex.Message}");
                }
            }
            _overlays.Clear();
        }
    }
}
