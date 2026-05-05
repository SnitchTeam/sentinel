using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;

namespace Oxide.Plugins
{
    public partial class Sentinel
    {
        private readonly List<string> _consoleBuffer = new();
        private const int ConsoleBufferMaxLines = 500;
        private const int ConsoleBufferEvictionBatch = 100;
        private const int ConsoleBufferEvictionThreshold = ConsoleBufferMaxLines + ConsoleBufferEvictionBatch;

        public void CaptureConsoleLine(string? message, string level = "INFO")
        {
            if (string.IsNullOrEmpty(message)) return;
            _consoleBuffer.Add($"[{level}] {message}");

            if (_consoleBuffer.Count >= ConsoleBufferEvictionThreshold)
            {
                _consoleBuffer.RemoveRange(0, ConsoleBufferEvictionBatch);
            }
        }

        public IReadOnlyList<string> ReadConsoleBuffer(string? filter = null)
        {
            if (string.IsNullOrEmpty(filter))
                return _consoleBuffer.ToList().AsReadOnly();

            var lowerFilter = filter.ToLowerInvariant();
            return _consoleBuffer
                .Where(line => line.ToLowerInvariant().Contains(lowerFilter))
                .ToList()
                .AsReadOnly();
        }

        public bool TryReadConsole(BasePlayer? admin, string? filter, out IReadOnlyList<string> lines, out string error)
        {
            error = "";
            if (!HasPermission(admin, "sentinel.console"))
            {
                error = "No permission";
                if (admin != null) NotifyNoPermission(admin);
                lines = new List<string>();
                return false;
            }

            lines = ReadConsoleBuffer(filter);
            return true;
        }

        [ConsoleCommand("sentinel.console.read")]
        void CCmdConsoleRead(ConsoleSystem.Arg arg)
        {
            var admin = arg.Player();
            if (!TryReadConsole(admin, null, out var lines, out var error))
            {
                Puts($"[Sentinel] Console read failed: {error}");
                return;
            }

            Puts($"[Sentinel] Console buffer ({lines.Count} lines):");
            foreach (var line in lines)
            {
                Puts(line);
            }
        }

        [ConsoleCommand("sentinel.console.filter")]
        void CCmdConsoleFilter(ConsoleSystem.Arg arg)
        {
            var admin = arg.Player();
            if (!TryReadConsole(admin, null, out _, out var error))
            {
                Puts($"[Sentinel] Console filter failed: {error}");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.console.filter <substring>");
                return;
            }

            var filter = arg.Args[0];
            var lines = ReadConsoleBuffer(filter);
            Puts($"[Sentinel] Console filter '{filter}' ({lines.Count} lines):");
            foreach (var line in lines)
            {
                Puts(line);
            }
        }
    }
}
