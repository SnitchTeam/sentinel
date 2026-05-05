using System;
using System.Collections.Generic;
using Oxide.Core;

namespace Oxide.Plugins
{
    public class SpectateState
    {
        public string AdminSteamId { get; set; } = "";
        public string? TargetSteamId { get; set; }
        public string TargetName { get; set; } = "";
        public Vector3 OriginalPosition { get; set; }
        public Vector3 OriginalRotation { get; set; }
        public bool IsSpectating { get; set; }
    }

    public partial class Sentinel
    {
        private readonly Dictionary<string, SpectateState> _spectateStates = new();

        // -------------------------------------------------------------
        // Terrain validation stubs (override in tests or real runtime)
        // -------------------------------------------------------------
        protected virtual float GetTerrainHeight(float x, float z)
        {
            // Real runtime: TerrainMeta.HeightMap.GetHeight(x, z)
            return 0f;
        }

        protected virtual float GetWaterLevel()
        {
            // Real runtime: WaterSystem.OceanLevel
            return -1000f;
        }

        protected virtual bool IsInsideBuilding(Vector3 position)
        {
            // Real runtime: Physics.OverlapSphere or GamePhysics.CheckSphere
            return false;
        }

        protected virtual bool IsValidTeleportDestination(Vector3 destination, out string error)
        {
            error = "";

            var terrainHeight = GetTerrainHeight(destination.x, destination.z);
            if (destination.y < terrainHeight - 0.5f)
            {
                error = "Destination is below terrain.";
                return false;
            }

            var waterLevel = GetWaterLevel();
            if (destination.y < waterLevel)
            {
                error = "Destination is under water.";
                return false;
            }

            if (IsInsideBuilding(destination))
            {
                error = "Destination is inside a building block.";
                return false;
            }

            return true;
        }

        protected virtual Vector3 CalculateSafeOffset(Vector3 position, Vector3 rotation, float distance = 1.5f)
        {
            var yawRadians = rotation.y * (float)Math.PI / 180f;
            var offset = new Vector3(
                MathF.Sin(yawRadians) * distance,
                0f,
                MathF.Cos(yawRadians) * distance
            );
            return position + offset;
        }

        // -------------------------------------------------------------
        // Spectate
        // -------------------------------------------------------------
        public bool ExecuteSpectate(BasePlayer? admin, string targetIdentifier, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.spectate"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "spectate", "enter", null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            if (admin == null)
            {
                error = "Spectate requires an in-game player.";
                LogAuditAction(actorId, actorName, null, null, "spectate", "enter", null, false);
                return false;
            }

            var target = ResolveTarget(targetIdentifier);
            if (target == null)
            {
                error = "Target player not found.";
                LogAuditAction(actorId, actorName, null, null, "spectate", "enter", null, false);
                return false;
            }

            if (_spectateStates.TryGetValue(actorId, out var existingState) && existingState.IsSpectating)
            {
                error = $"Already spectating {existingState.TargetName} — exit first";
                LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "spectate", "enter", null, false,
                    $"{{\"targetSteamId\":\"{target.UserIDString}\",\"action\":\"enter\",\"error\":\"Already spectating {existingState.TargetName}\"}}");
                return false;
            }

            // Save original state
            _spectateStates[actorId] = new SpectateState
            {
                AdminSteamId = actorId,
                TargetSteamId = target.UserIDString,
                TargetName = target.displayName,
                OriginalPosition = admin.Position,
                OriginalRotation = admin.Rotation,
                IsSpectating = true
            };

            // Enter spectate mode
            admin.SetPlayerFlag("Spectating", true);
            admin.UpdateSpectating();
            admin.SendNetworkUpdate();

            LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "spectate", "enter", null, true,
                $"{{\"targetSteamId\":\"{target.UserIDString}\",\"action\":\"enter\"}}");
            return true;
        }

        public bool ExecuteExitSpectate(BasePlayer? admin, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.spectate"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "spectate", "exit", null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            if (admin == null)
            {
                error = "Spectate exit requires an in-game player.";
                LogAuditAction(actorId, actorName, null, null, "spectate", "exit", null, false);
                return false;
            }

            if (!_spectateStates.TryGetValue(actorId, out var state) || !state.IsSpectating)
            {
                error = "You are not currently spectating.";
                LogAuditAction(actorId, actorName, null, null, "spectate", "exit", null, false);
                return false;
            }

            // Restore original position and rotation
            admin.Position = state.OriginalPosition;
            admin.Rotation = state.OriginalRotation;
            admin.SetPlayerFlag("Spectating", false);
            admin.UpdateSpectating();
            admin.SendNetworkUpdate();

            state.IsSpectating = false;
            _spectateStates.Remove(actorId);

            LogAuditAction(actorId, actorName, state.TargetSteamId, null, "spectate", "exit", null, true,
                $"{{\"targetSteamId\":\"{state.TargetSteamId ?? "null"}\",\"action\":\"exit\"}}");
            return true;
        }

        public bool IsSpectating(string steamId)
        {
            return _spectateStates.TryGetValue(steamId, out var state) && state.IsSpectating;
        }

        public SpectateState? GetSpectateState(string steamId)
        {
            _spectateStates.TryGetValue(steamId, out var state);
            return state;
        }

        // -------------------------------------------------------------
        // Teleport
        // -------------------------------------------------------------
        public bool ExecuteTeleportTo(BasePlayer? admin, string targetIdentifier, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.teleport"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "teleport", "tpto", null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            if (admin == null)
            {
                error = "TPto requires an in-game player.";
                LogAuditAction(actorId, actorName, null, null, "teleport", "tpto", null, false);
                return false;
            }

            var target = ResolveTarget(targetIdentifier);
            if (target == null)
            {
                error = "Target player not found.";
                LogAuditAction(actorId, actorName, null, null, "teleport", "tpto", null, false);
                return false;
            }

            var destination = CalculateSafeOffset(target.Position, target.Rotation, 1.5f);

            if (!IsValidTeleportDestination(destination, out error))
            {
                LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "teleport", "tpto", null, false,
                    $"{{\"error\":\"{error}\",\"toX\":{destination.x},\"toY\":{destination.y},\"toZ\":{destination.z}}}");
                return false;
            }

            var fromPos = admin.Position;
            admin.Position = destination;
            admin.SendNetworkUpdate();

            LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "teleport", "tpto", null, true,
                $"{{\"action\":\"tpto\",\"fromX\":{fromPos.x},\"fromY\":{fromPos.y},\"fromZ\":{fromPos.z},\"toX\":{destination.x},\"toY\":{destination.y},\"toZ\":{destination.z}}}");
            return true;
        }

        public bool ExecuteTeleportMe(BasePlayer? admin, string targetIdentifier, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.teleport"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "teleport", "tpme", null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            if (admin == null)
            {
                error = "TPme requires an in-game player.";
                LogAuditAction(actorId, actorName, null, null, "teleport", "tpme", null, false);
                return false;
            }

            var target = ResolveTarget(targetIdentifier);
            if (target == null)
            {
                error = "Target player not found.";
                LogAuditAction(actorId, actorName, null, null, "teleport", "tpme", null, false);
                return false;
            }

            var destination = CalculateSafeOffset(admin.Position, admin.Rotation, 1.5f);

            if (!IsValidTeleportDestination(destination, out error))
            {
                LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "teleport", "tpme", null, false,
                    $"{{\"error\":\"{error}\",\"toX\":{destination.x},\"toY\":{destination.y},\"toZ\":{destination.z}}}");
                return false;
            }

            var fromPos = target.Position;
            target.Position = destination;
            target.SendNetworkUpdate();

            LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "teleport", "tpme", null, true,
                $"{{\"action\":\"tpme\",\"fromX\":{fromPos.x},\"fromY\":{fromPos.y},\"fromZ\":{fromPos.z},\"toX\":{destination.x},\"toY\":{destination.y},\"toZ\":{destination.z}}}");
            return true;
        }

        // -------------------------------------------------------------
        // Console Commands
        // -------------------------------------------------------------
        [ConsoleCommand("sentinel.spectate")]
        void CCmdSpectate(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.spectate \u003cplayer\u003e | sentinel.spectate exit");
                return;
            }

            var subcommand = arg.Args[0].ToLowerInvariant();
            var admin = arg.Player();

            if (subcommand == "exit")
            {
                if (!ExecuteExitSpectate(admin, out var error))
                {
                    Puts($"[Sentinel] Exit spectate failed: {error}");
                }
                else
                {
                    Puts($"[Sentinel] Exited spectate mode.");
                }
            }
            else
            {
                if (!ExecuteSpectate(admin, arg.Args[0], out var error))
                {
                    Puts($"[Sentinel] Spectate failed: {error}");
                }
                else
                {
                    Puts($"[Sentinel] Now spectating {arg.Args[0]}.");
                }
            }
        }

        [ConsoleCommand("sentinel.tpto")]
        void CCmdTeleportTo(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.tpto \u003cplayer\u003e");
                return;
            }

            var admin = arg.Player();
            if (!ExecuteTeleportTo(admin, arg.Args[0], out var error))
            {
                Puts($"[Sentinel] TPto failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Teleported to {arg.Args[0]}.");
            }
        }

        [ConsoleCommand("sentinel.tpme")]
        void CCmdTeleportMe(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.tpme \u003cplayer\u003e");
                return;
            }

            var admin = arg.Player();
            if (!ExecuteTeleportMe(admin, arg.Args[0], out var error))
            {
                Puts($"[Sentinel] TPme failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Teleported {arg.Args[0]} to you.");
            }
        }
    }
}
