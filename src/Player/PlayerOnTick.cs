/*
Copyright (C) 2024 Dea Brcka

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using FixVectorLeak;

namespace SharpTimer
{
    public partial class SharpTimer
    {
        public void PlayerOnTick()
        {
            try
            {
                int currentTick = Server.TickCount;

                foreach (CCSPlayerController player in connectedPlayers.Values)
                {
                    if (player == null || !player.IsValid) continue;

                    int slot = player.Slot;
                    string playerName = player.PlayerName;
                    string steamID = player.SteamID.ToString();

                    if ((CsTeam)player.TeamNum == CsTeam.Spectator || !player.PawnIsAlive)
                    {
                        if (currentTick % (64 / hudTickrate) != 0)
                            continue;

                        SpectatorOnTick(player);
                        continue;
                    }

                    if (playerTimers.TryGetValue(slot, out PlayerTimerInfo? playerTimer))
                    {
                        if (playerTimer.IsAddingStartZone || playerTimer.IsAddingEndZone || playerTimer.IsAddingBonusStartZone || playerTimer.IsAddingBonusEndZone)
                        {
                            OnTickZoneTool(player);
                            continue;
                        }

                        if (!IsAllowedPlayer(player))
                        {
                            InvalidateTimer(player);
                            continue;
                        }

                        var playerPawn = player.PlayerPawn?.Value;
                        if (playerPawn == null) continue;

                        PlayerButtons? playerButtons = player.Buttons;
                        Vector_t playerSpeed = playerPawn.AbsVelocity.ToVector_t();
                        bool isTimerBlocked = playerTimer.IsTimerBlocked;

                        /* afk */
                        if (connectedAFKPlayers.ContainsKey(player.Slot))
                        {
                            if (!playerSpeed.IsZero())
                            {
                                connectedAFKPlayers.Remove(player.Slot);
                                playerTimer.AFKWarned = false;
                                playerTimer.AFKTicks = 0;
                            }
                            else continue;
                        }

                        if (playerTimer.AFKTicks >= afkSeconds*48 && !playerTimer.AFKWarned && afkWarning)
                        {
                            Utils.PrintToChat(player, $"{Localizer["afk_message"]}");
                            playerTimer.AFKWarned = true;
                        }
                            
                        if (playerTimer.AFKTicks >= afkSeconds*64)
                            connectedAFKPlayers[player.Slot] = connectedPlayers[player.Slot];

                        if (playerSpeed.IsZero())
                            playerTimer.AFKTicks++;
                        else
                            playerTimer.AFKTicks = 0;
                        /* afk */

                        /* timer counting */
                        bool isTimerRunning = playerTimer.IsTimerRunning;
                        bool isBonusTimerRunning = playerTimer.IsBonusTimerRunning;

                        if (isTimerRunning)
                        {
                            playerTimer.TimerTicks++;

                            if (useStageTriggers)
                                playerTimer.StageTicks++;
                        }
                        else if (isBonusTimerRunning)
                        {
                            playerTimer.BonusTimerTicks++;
                        }
                        /* timer counting */

                        // remove jumping in startzone
                        if (!startzoneJumping && playerTimer.inStartzone)
                        {
                            if((playerButtons & PlayerButtons.Jump) != 0 || playerTimer.MovementService!.LegacyJump.OldJumpPressed)
                                playerPawn.AbsVelocity.Z = 0f;
                        }

                        /* single startzone jump */
                        if (startzoneSingleJumpEnabled && !playerTimer.IsTimerBlocked)
                        {
                            bool wasOnGround = playerTimer.WasOnGroundLastTick;
                            playerTimer.WasOnGroundLastTick = playerPawn.GroundEntity.IsValid;
                            
                            if ((playerTimer.inStartzone || playerTimer.CurrentZoneInfo.InBonusStartZone) && playerTimer.StartZoneJumps >= 1 &&
                                playerPawn.AbsVelocity.IsZero())
                                playerTimer.StartZoneJumps = 0;

                            if ((playerTimer.inStartzone || playerTimer.CurrentZoneInfo.InBonusStartZone) && !wasOnGround && playerPawn.GroundEntity.IsValid)
                                playerTimer.StartZoneJumps++;

                            if (playerTimer.StartZoneJumps == 1 && !wasOnGround)
                            {
                                playerPawn.AbsVelocity.X = 0;
                                playerPawn.AbsVelocity.Y = 0;
                            }
                        }
                        /* single startzone jump */

                        /* hide weapons */
                        bool hasWeapons = playerPawn.WeaponServices?.MyWeapons?.Count > 0;
                        if (playerTimer.HideWeapon)
                        {
                            if (hasWeapons)
                            {
                                player.RemoveWeapons();
                                playerTimer.GivenWeapon = false;
                            }
                        }
                        else
                        {
                            if (!hasWeapons && !playerTimer.GivenWeapon)
                            {
                                if (player.TeamT())
                                {
                                    player.RemoveWeapons();
                                    player.GiveNamedItem("weapon_knife_t");
                                    player.GiveNamedItem("weapon_glock");
                                }
                                else if (player.TeamCT())
                                {
                                    player.RemoveWeapons();
                                    player.GiveNamedItem("weapon_knife");
                                    player.GiveNamedItem("weapon_usp_silencer");
                                }

                                playerTimer.GivenWeapon = true;
                            }
                        }
                        /* hide weapons */

                        /* styles */
                        if (playerTimer.currentStyle.Equals(4)) //check if 400vel
                            SetVelocity(player, playerPawn.AbsVelocity.ToVector_t(), 400);

                        if (playerTimer.currentStyle.Equals(10) && !playerPawn.GroundEntity.IsValid && currentTick % 2 != 0) //check if ff
                            IncreaseVelocity(player);

                        if (playerTimer.changedStyle)
                        {
                            _ = Task.Run(async () => await RankCommandHandler(player, steamID, slot, playerName, true, playerTimer.currentStyle, playerTimer.Mode));
                            playerTimer.changedStyle = false;
                        }
                        /* styles */
                        
                        /* modes */
                        if (playerTimer.ChangedMode)
                        {
                            _ = Task.Run(async () => await RankCommandHandler(player, steamID, slot, playerName, true, playerTimer.currentStyle, playerTimer.Mode));
                            playerTimer.ChangedMode = false;
                        }
                        /* modes */

                        // respawn player if on bhop block too long
                        bool isOnBhopBlock = playerTimer.IsOnBhopBlock;
                        if (isOnBhopBlock)
                        {
                            playerTimer.TicksOnBhopBlock++;

                            if (playerTimer.TicksOnBhopBlock > bhopBlockTime)
                                RespawnPlayer(player);
                        }

                        /* checking if player in zones */
                        if (useTriggers == false && isTimerBlocked == false)
                            CheckPlayerCoords(player, playerSpeed);

                        // idk why there is another one but replay breaks with them merged into one :D
                        if (useTriggers == true && isTimerBlocked == false && useTriggersAndFakeZones)
                            CheckPlayerCoords(player, playerSpeed);
                        /* checking if player in zones */

                        /* hud strafe sync % */
                        if (StrafeHudEnabled)
                            OnSyncTick(player, playerButtons, playerPawn.EyeAngles!);

                        // reset in startzone
                        if (StrafeHudEnabled && playerTimer.inStartzone && playerTimer.Rotation.Count > 0) 
                        { 
                            playerTimer.Sync = 100.00f;
                            playerTimer.Rotation.Clear();
                        }
                        /* hud strafe sync % */

                        if (forcePlayerSpeedEnabled)
                        {
                            string designerName = playerPawn.WeaponServices!.ActiveWeapon?.Value?.DesignerName ?? "no_knife";
                            ForcePlayerSpeed(player, designerName);
                        }

                        /* ranks */
                        if (playerTimer.IsRankPbCached == false)
                        {
                            Utils.LogDebug($"{playerName} has rank and pb null... calling handler");
                            _ = Task.Run(async () => await RankCommandHandler(player, steamID, slot, playerName, true, playerTimer.currentStyle, playerTimer.Mode));

                            playerTimer.IsRankPbCached = true;
                        }

                        // attempted bugfix on rank not appearing
                        if (playerTimer.CachedMapPlacement == null && !playerTimer.IsRankPbReallyCached)
                        {
                            Utils.LogDebug($"{playerName} CachedMapPlacement is still null, calling rank handler once more");
                            playerTimer.IsRankPbReallyCached = true;
                            AddTimer(3.0f, () => { _ = Task.Run(async () => await RankCommandHandler(player, steamID, slot, playerName, true, playerTimer.currentStyle, playerTimer.Mode)); });                           
                        }
                        /* ranks */

                        if (playerTimer.IsSpecTargetCached == false || specTargets.ContainsKey(playerPawn.EntityHandle.Index) == false)
                        {
                            specTargets[playerPawn.EntityHandle.Index] = new CCSPlayerController(player.Handle);
                            playerTimer.IsSpecTargetCached = true;
                            Utils.LogDebug($"{playerName} was not in specTargets, adding...");
                        }

                        if (removeCollisionEnabled)
                        {
                            if (playerPawn.Collision.CollisionGroup != (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING ||
                                playerPawn.Collision.CollisionAttribute.CollisionGroup != (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING
                                )
                            {
                                Utils.LogDebug($"{playerName} has wrong collision group... RemovePlayerCollision");
                                RemovePlayerCollision(player);
                            }
                        }

                        if (removeCrouchFatigueEnabled)
                        {
                            if (playerTimer.MovementService != null && playerTimer.MovementService.DuckSpeed != 7.0f)
                                playerTimer.MovementService.DuckSpeed = 7.0f;
                        }

                        // update pre speed for hud
                        if (((PlayerFlags)playerPawn.Flags & PlayerFlags.FL_ONGROUND) != PlayerFlags.FL_ONGROUND)
                        {
                            playerTimer.TicksInAir++;

                            if (playerTimer.TicksInAir == 1)
                                playerTimer.PreSpeed = $"{playerSpeed.X} {playerSpeed.Y} {playerSpeed.Z}";
                        }
                        else playerTimer.TicksInAir = 0;

                        // replays
                        if (enableReplays)
                        {
                            int timerTicks = playerTimer.TimerTicks;
                            if (!playerTimer.IsReplaying && (timerTicks > 0 || playerTimer.BonusTimerTicks > 0) && playerTimer.IsRecordingReplay && !isTimerBlocked)
                                ReplayUpdate(player, timerTicks);

                            if (playerTimer.IsReplaying && !playerTimer.IsRecordingReplay && isTimerBlocked)
                                ReplayPlay(player);

                            else if (
                                !isTimerBlocked &&
                                (playerPawn.MoveType.HasFlag(MoveType_t.MOVETYPE_OBSERVER) || playerPawn.ActualMoveType.HasFlag(MoveType_t.MOVETYPE_OBSERVER)) &&
                                !playerPawn.MoveType.HasFlag(MoveType_t.MOVETYPE_LADDER))
                            {
                                SetMoveType(player, MoveType_t.MOVETYPE_WALK);
                            }
                        }

                        // timer hud content
                        if (currentTick % (64 / hudTickrate) != 0)
                            continue;

                        string hudContent = GetHudContent(playerTimer, player);

                        if (!string.IsNullOrEmpty(hudContent))
                            player.PrintToCenterHtml(hudContent);
                        
                        // idk what this is for
                        playerTimer.MovementService!.LegacyJump.OldJumpPressed = false;
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message != "Invalid game event") Utils.LogError($"Error in TimerOnTick: {ex.StackTrace}");
            }
        }

        // Dispatch to the correct layout based on per-player preference
        private string GetHudContent(PlayerTimerInfo playerTimer, CCSPlayerController player)
        {
            return playerTimer.HudLayout == 2
                ? GetHudContentLayout2(playerTimer, player)
                : GetHudContentLayout1(playerTimer, player);
        }

        // ──────────────────────────────────────────────────────────────────────
        // LAYOUT 1 — Trackmania-inspired (default)
        // Top-left: leaderboard
        // Top-center: checkpoint diff flash (2.5 s)
        // Bottom-center: speed, sync, info, timer
        // Bottom-left: keys
        // ──────────────────────────────────────────────────────────────────────
        private string GetHudContentLayout1(PlayerTimerInfo playerTimer, CCSPlayerController player)
        {
            bool isTimerRunning   = playerTimer.IsTimerRunning;
            bool isBonusRunning   = playerTimer.IsBonusTimerRunning;
            PlayerButtons? btns   = player.Buttons;
            Vector_t vel          = player.PlayerPawn!.Value!.AbsVelocity.ToVector_t();
            bool keyEnabled       = !playerTimer.HideKeys && !playerTimer.IsReplaying && keysOverlayEnabled;
            bool hudEnabled       = !playerTimer.HideTimerHud && hudOverlayEnabled;

            if (!hudEnabled && !keyEnabled) return "";

            // velocity
            string fmtVel = Math.Round(use2DSpeed ? vel.Length2D() : vel.Length()).ToString("0000");
            int iVel = int.Parse(fmtVel);
            string velColor = secondaryHUDcolor;
            if (useDynamicColor)
            {
                int[] thresholds = { 349, 699, 1049, 1399, 1749, 2099, 2449, 2799, 3149, 3499 };
                string[] colors  = { "LimeGreen","Lime","GreenYellow","Yellow","Gold","Orange","DarkOrange","Tomato","OrangeRed","Red","Crimson" };
                velColor = "Crimson";
                for (int i = 0; i < thresholds.Length; i++) { if (iVel < thresholds[i]) { velColor = colors[i]; break; } }
            }

            string playerTime   = isTimerRunning  ? Utils.FormatTime(playerTimer.TimerTicks) :
                                   isBonusRunning ? Utils.FormatTime(playerTimer.BonusTimerTicks) : "--:--.---";
            string bonusTime    = Utils.FormatTime(playerTimer.BonusTimerTicks);
            string fmtPre       = Math.Round(Utils.ParseVector_t(playerTimer.PreSpeed ?? "0 0 0").Length2D()).ToString("000");

            var sb = new System.Text.StringBuilder();

            // ── LEADERBOARD (top, left-aligned) ─────────────────────────────
            if (hudEnabled && !playerTimer.IsReplaying)
            {
                var records = GetSortedRecordsForMode(playerTimer.Mode);
                if (records != null && records.Count > 0)
                {
                    sb.Append("<div align='left'>");
                    sb.Append($"<font class='fontSize-s stratum-light-mono' color='white'>");

                    int top = Math.Min(5, records.Count);
                    bool playerInTop = int.TryParse(playerTimer.CachedMapPlacement, out int pRank) && pRank <= 5;
                    string mySteamId = player.SteamID.ToString();

                    for (int i = 1; i <= top; i++)
                    {
                        if (!records.TryGetValue(i, out var rec)) continue;
                        string rowColor = rec.SteamID == mySteamId ? primaryHUDcolor : "white";
                        string rank = i.ToString().PadLeft(2, '\u00A0');
                        string name = PadHtml(rec.PlayerName, 14);
                        string time = Utils.FormatTime(rec.TimerTicks);
                        sb.Append($"<font color='{rowColor}'>{rank}\u00A0\u00A0{name}\u00A0{time}</font><br>");
                    }

                    // Player's own row if outside top 5
                    if (!playerInTop && pRank > 0 && !string.IsNullOrEmpty(playerTimer.CachedPB))
                    {
                        string sepLine = new string('\u2500', 24);
                        sb.Append($"<font color='gray'>{sepLine}</font><br>");
                        string rank = pRank.ToString().PadLeft(2, '\u00A0');
                        string name = PadHtml(player.PlayerName, 14);
                        string pb   = (playerTimer.CachedPB ?? "").Trim();
                        sb.Append($"<font color='gray'>{rank}\u00A0\u00A0{name}\u00A0{pb}</font><br>");
                    }

                    sb.Append("</font></div>");
                }
            }

            // ── CHECKPOINT DIFF FLASH (top-center, 2.5 s) ───────────────────
            if (hudEnabled && !string.IsNullOrEmpty(playerTimer.CheckpointDiffText)
                           && DateTime.Now < playerTimer.CheckpointDiffExpiry)
            {
                bool isFaster = playerTimer.CheckpointDiffFaster;
                string diffBg = isFaster ? "rgba(0,90,210,0.85)" : "rgba(190,20,20,0.85)";
                string speedBg = isFaster ? "rgba(30,140,30,0.85)" : "rgba(180,90,0,0.85)";

                sb.Append("<div align='center'>");

                // row 1: placement + cumulative time
                if (!string.IsNullOrEmpty(playerTimer.CheckpointFlashPlacement))
                    sb.Append($"<font class='fontSize-s stratum-bold-italic' " +
                              $"style='background-color:rgba(0,0,0,0.65);'>" +
                              $"\u00A0{playerTimer.CheckpointFlashPlacement}" +
                              $"\u00A0\u00A0{playerTimer.CheckpointFlashTime}\u00A0</font><br>");

                // row 2: time diff (blue = faster, red = slower)
                sb.Append($"<font class='fontSize-m stratum-bold-italic' " +
                          $"style='background-color:{diffBg};'>" +
                          $"\u00A0{playerTimer.CheckpointDiffText}\u00A0</font><br>");

                // row 3: speed at checkpoint + diff (green = faster, orange = slower)
                if (!string.IsNullOrEmpty(playerTimer.CheckpointFlashSpeed))
                    sb.Append($"<font class='fontSize-s stratum-light-mono' " +
                              $"style='background-color:rgba(30,30,30,0.75);'>" +
                              $"\u00A0{playerTimer.CheckpointFlashSpeed}\u00A0</font>" +
                              $"<font class='fontSize-s stratum-bold-italic' " +
                              $"style='background-color:{speedBg};'>" +
                              $"\u00A0{(isFaster ? "▲" : "▼")}\u00A0</font><br>");

                sb.Append("</div>");
            }
            else
            {
                // spacer so the rest of the layout doesn't jump
                sb.Append("<br><br>");
            }

            if (hudEnabled)
            {
                // ── SPEED ────────────────────────────────────────────────────
                if (VelocityHudEnabled)
                {
                    string gif = playerTimer.IsTester ? (playerTimer.TesterSmolGif ?? "") : "";
                    sb.Append($"{gif}<font class='fontSize-l horizontal-center' color='{velColor}'>{fmtVel}</font>" +
                              $"<font class='fontSize-s stratum-bold-italic' color='gray'>\u00A0({fmtPre})</font>{gif}<br>");
                }

                // ── SYNC ─────────────────────────────────────────────────────
                if (StrafeHudEnabled && !playerTimer.IsReplaying)
                    sb.Append($"<font class='fontSize-s stratum-bold-italic' color='{secondaryHUDcolor}'>{playerTimer.Sync:F2}%</font><br>");

                // ── INFO LINE (mode / stage / style) ─────────────────────────
                string infoLine = BuildInfoLine(playerTimer);
                if (!string.IsNullOrEmpty(infoLine))
                    sb.Append($"<font class='fontSize-s stratum-bold-italic' color='gray'>{infoLine}</font><br>");

                // ── TIMER ────────────────────────────────────────────────────
                if (isBonusRunning)
                {
                    sb.Append($"<font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'>Bonus #{playerTimer.BonusStage}\u00A0</font>" +
                              $"<font class='fontSize-l horizontal-center' color='{primaryHUDcolor}'>{bonusTime}</font><br>");
                }
                else if (playerTimer.IsReplaying)
                {
                    sb.Append($"<font class='horizontal-center' color='red'>◉ REPLAY {Utils.FormatTime(playerReplays[player.Slot].CurrentPlaybackFrame)}</font><br>");
                }
                else
                {
                    string place = !string.IsNullOrEmpty(playerTimer.CachedMapPlacement)
                        ? $"\u00A0(#{playerTimer.CachedMapPlacement})" : "";
                    sb.Append($"<font class='fontSize-l horizontal-center' color='{primaryHUDcolor}'>{playerTime}</font>" +
                              $"<font class='fontSize-s stratum-bold-italic' color='gray'>{place}</font><br>");
                }
            }

            // ── KEYS (bottom, left-aligned) ───────────────────────────────────
            if (keyEnabled && !playerTimer.IsReplaying)
            {
                if (hudEnabled) sb.Append("<br>");
                string a = (btns & PlayerButtons.Moveleft)  != 0 ? "A" : "_";
                string w = (btns & PlayerButtons.Forward)   != 0 ? "W" : "_";
                string d = (btns & PlayerButtons.Moveright) != 0 ? "D" : "_";
                string s = (btns & PlayerButtons.Back)      != 0 ? "S" : "_";
                string j = ((btns & PlayerButtons.Jump)     != 0 || playerTimer.MovementService!.LegacyJump.OldJumpPressed) ? "J" : "_";
                string c = (btns & PlayerButtons.Duck)      != 0 ? "C" : "_";
                sb.Append($"<div align='left'><font class='fontSize-ml stratum-light-mono' color='{tertiaryHUDcolor}'>" +
                          $"{a} {w} {d} {s} {j} {c}</font></div>");
            }

            return sb.ToString();
        }

        // ──────────────────────────────────────────────────────────────────────
        // LAYOUT 2 — original layout (preserved as-was)
        // ──────────────────────────────────────────────────────────────────────
        private string GetHudContentLayout2(PlayerTimerInfo playerTimer, CCSPlayerController player)
        {
            bool isTimerRunning    = playerTimer.IsTimerRunning;
            bool isBonusTimerRunning = playerTimer.IsBonusTimerRunning;
            int timerTicks         = playerTimer.TimerTicks;
            PlayerButtons? playerButtons = player.Buttons;
            Vector_t playerSpeed   = player.PlayerPawn!.Value!.AbsVelocity.ToVector_t();
            bool keyEnabled        = !playerTimer.HideKeys && !playerTimer.IsReplaying && keysOverlayEnabled;
            bool hudEnabled        = !playerTimer.HideTimerHud && hudOverlayEnabled;

            string formattedPlayerVel = Math.Round(use2DSpeed
                ? playerSpeed.Length2D()
                : playerSpeed.Length())
                .ToString("0000");

            int playerVel = int.Parse(formattedPlayerVel);

            string secondaryHUDcolorDynamic = "LimeGreen";
            int[] velocityThresholds = { 349, 699, 1049, 1399, 1749, 2099, 2449, 2799, 3149, 3499 };
            string[] hudColors = { "LimeGreen", "Lime", "GreenYellow", "Yellow", "Gold", "Orange", "DarkOrange", "Tomato", "OrangeRed", "Red", "Crimson" };
            for (int i = 0; i < velocityThresholds.Length; i++)
            {
                if (playerVel < velocityThresholds[i]) { secondaryHUDcolorDynamic = hudColors[i]; break; }
            }

            string playerVelColor = useDynamicColor ? secondaryHUDcolorDynamic : secondaryHUDcolor;
            string formattedPlayerPre = Math.Round(Utils.ParseVector_t(playerTimer.PreSpeed ?? "0 0 0").Length2D()).ToString("000");
            string playerTime = Utils.FormatTime(timerTicks);
            string playerBonusTime = Utils.FormatTime(playerTimer.BonusTimerTicks);

            string timerLine =
                isBonusTimerRunning
                    ? $" <font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'>Bonus #{playerTimer.BonusStage} Timer:</font> " +
                        $"<font class='fontSize-l horizontal-center' color='{primaryHUDcolor}'>{playerBonusTime}</font> <br>"
                    : isTimerRunning
                        ? $" <font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'>Timer: </font>" +
                            $"<font class='fontSize-l horizontal-center' color='{primaryHUDcolor}'>{playerTime}</font> " +
                            $"<font color='gray' class='fontSize-s stratum-bold-italic'>({GetPlayerPlacement(player)})</font>" +
                            $"{((playerTimer.CurrentMapStage != 0 && useStageTriggers == true) ? $" <font color='gray' class='fontSize-s stratum-bold-italic'> {playerTimer.CurrentMapStage}/{stageTriggerCount}</font>" : "")} <br>"
                        : playerTimer.IsReplaying
                            ? $" <font class='horizontal-center' color='red'>◉ REPLAY {Utils.FormatTime(playerReplays[player.Slot].CurrentPlaybackFrame)}</font> <br>"
                            : "";

            string veloLine =
                $" {(playerTimer.IsTester ? playerTimer.TesterSmolGif : "")}" +
                $"<font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'>Speed:</font> " +
                $"{(playerTimer.IsReplaying ? "<font class=''" : "<font class='fontSize-l horizontal-center'")} color='{playerVelColor}'>{formattedPlayerVel}</font> " +
                $"<font class='fontSize-s stratum-bold-italic' color='gray'>({formattedPlayerPre})</font>{(playerTimer.IsTester ? playerTimer.TesterSmolGif : "")} <br>";

            string syncLine =
                $"<font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'>Sync:</font> " +
                $"<font class='fontSize-l horizontal-center' color='{secondaryHUDcolor}'>{playerTimer.Sync:F2}%</font> <br>";

            string infoLine = playerTimer.CurrentZoneInfo.InBonusStartZone
                ? GetBonusInfoLine(playerTimer)
                : GetMainMapInfoLine(playerTimer);

            string keysLineNoHtml = $"{(hudEnabled ? "<br>" : "")}<font class='fontSize-ml stratum-light-mono' color='{tertiaryHUDcolor}'>" +
                $"{((playerButtons & PlayerButtons.Moveleft)  != 0 ? "A" : "_")} " +
                $"{((playerButtons & PlayerButtons.Forward)   != 0 ? "W" : "_")} " +
                $"{((playerButtons & PlayerButtons.Moveright) != 0 ? "D" : "_")} " +
                $"{((playerButtons & PlayerButtons.Back)      != 0 ? "S" : "_")} " +
                $"{((playerButtons & PlayerButtons.Jump)      != 0 || playerTimer.MovementService!.LegacyJump.OldJumpPressed ? "J" : "_")} " +
                $"{((playerButtons & PlayerButtons.Duck)      != 0 ? "C" : "_")}";

            return (hudEnabled
                        ? timerLine +
                          (VelocityHudEnabled ? veloLine : "") +
                          (StrafeHudEnabled && !playerTimer.IsReplaying ? syncLine : "") +
                          infoLine
                        : "") +
                   (keyEnabled && !playerTimer.IsReplaying ? keysLineNoHtml : "");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers shared by both layouts
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Pad a string to <paramref name="width"/> chars using non-breaking spaces.</summary>
        private static string PadHtml(string? input, int width)
        {
            var s = input ?? "";
            if (s.Length > width) s = s.Substring(0, width - 1) + "…";
            return s.PadRight(width, '\u00A0');
        }

        /// <summary>Returns the sorted record dictionary for the given mode name.</summary>
        private Dictionary<int, PlayerRecord>? GetSortedRecordsForMode(string mode) =>
            mode?.ToLower() switch
            {
                "standard" => SortedCachedStandardRecords,
                "85t"      => SortedCached85tRecords,
                "102t"     => SortedCached102tRecords,
                "128t"     => SortedCached128tRecords,
                "source"   => SortedCachedSourceRecords,
                "bhop"     => SortedCachedBhopRecords,
                _          => SortedCachedStandardRecords
            };

        /// <summary>Builds the compact info line: "[stage/total] | Mode | Style"</summary>
        private string BuildInfoLine(PlayerTimerInfo playerTimer)
        {
            if (playerTimer.CurrentZoneInfo.InBonusStartZone)
            {
                var bn = playerTimer.CurrentZoneInfo.CurrentBonusNumber;
                if (bn != 0)
                {
                    var ci = playerTimer.CachedBonusInfo.FirstOrDefault(x => x.Key == bn);
                    string pbStr = ci.Value != null ? Utils.FormatTime(ci.Value.PbTicks) : "--:--.---";
                    string place = ci.Value != null ? $" ({ci.Value.Placement})" : "";
                    return $"Bonus #{bn}{place}\u00A0|\u00A0{playerTimer.Mode}{pbStr}";
                }
            }

            string stageStr = (useStageTriggers && playerTimer.CurrentMapStage != 0)
                ? $"[{playerTimer.CurrentMapStage}/{stageTriggerCount}]\u00A0|\u00A0" : "";
            string styleStr = (enableStyles && playerTimer.currentStyle != 0)
                ? $"\u00A0|\u00A0{GetNamedStyle(playerTimer.currentStyle)}" : "";
            return $"{stageStr}{playerTimer.Mode}{styleStr}";
        }

        private string GetMainMapInfoLine(PlayerTimerInfo playerTimer)
        {
            return !playerTimer.IsReplaying
                ? $"<font class='fontSize-s stratum-bold-italic' color='gray'>" +
                    $"{playerTimer.CachedPB} " +
                    $"[{playerTimer.CachedMapPlacement}] " +
                    $"{(RankIconsEnabled ? $" |</font> <img src='{playerTimer.RankHUDIcon}'><font class='fontSize-s stratum-bold-italic' color='gray'>" : "")}" +
                    $"{(enableStyles && playerTimer.currentStyle != 0 ? $" | {GetNamedStyle(playerTimer.currentStyle)}" : "")} | {playerTimer.Mode}<br>" +
                    $"{GetMapDataLine()}" +
                    $"</font>"
                : $" <font class='fontSize-s stratum-bold-italic' color='gray'>{playerTimer.ReplayHUDString}</font>";
        }
        
        private string GetMapDataLine()
        {
            string mapInfo = "";
            if (MapTierHudEnabled && currentMapTier != null)
                mapInfo += $"Tier: {currentMapTier}";
            if (MapTypeHudEnabled && currentMapType != null)
                mapInfo += (string.IsNullOrEmpty(mapInfo) ? "" : " | ") + currentMapType;
            if (MapNameHudEnabled && currentMapType == null && currentMapTier == null)
                mapInfo += (string.IsNullOrEmpty(mapInfo) ? "" : " | ") + currentMapName;
            return mapInfo;
        }

        private string GetBonusInfoLine(PlayerTimerInfo playerTimer)
        {
            var currentBonusNumber = playerTimer.CurrentZoneInfo.CurrentBonusNumber;
            if (currentBonusNumber != 0)
            {
                var cachedBonusInfo = playerTimer.CachedBonusInfo.FirstOrDefault(x => x.Key == currentBonusNumber);
                return !playerTimer.IsReplaying
                    ? $"<font class='fontSize-s stratum-bold-italic' color='gray'>" +
                        $"{(cachedBonusInfo.Value != null ? $"{Utils.FormatTime(cachedBonusInfo.Value.PbTicks)}" : "Unranked")}" +
                        $"{(cachedBonusInfo.Value != null ? $" ({cachedBonusInfo.Value.Placement})" : "")}</font>" +
                        $"<font class='fontSize-s stratum-bold-italic' color='gray'>" +
                        $"{(enableStyles && playerTimer.currentStyle != 0 ? $" | {GetNamedStyle(playerTimer.currentStyle)}" : "")} | {playerTimer.Mode}<br>" +
                        $" | Bonus #{currentBonusNumber} </font>"
                    : $" <font class='fontSize-s stratum-bold-italic' color='gray'>{playerTimer.ReplayHUDString}</font>";
            }
            return GetMainMapInfoLine(playerTimer);
        }

        public void SpectatorOnTick(CCSPlayerController player)
        {
            if (!IsAllowedSpectator(player))
                return;

            try
            {
                var target = specTargets[player.Pawn.Value!.ObserverServices!.ObserverTarget.Index];
                if (playerTimers.TryGetValue(target.Slot, out PlayerTimerInfo? playerTimer) && IsAllowedPlayer(target))
                {
                    string hudContent = GetHudContent(playerTimer, target);

                    if (!string.IsNullOrEmpty(hudContent))
                        player.PrintToCenterHtml(hudContent);
                }
            }
            catch (Exception ex)
            {
                if (ex.Message != "Invalid game event") Utils.LogError($"Error in SpectatorOnTick: {ex.Message}");
            }
        }
    }
}