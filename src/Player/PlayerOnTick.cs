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

                        bool hudEnabled = !playerTimer.HideTimerHud && hudOverlayEnabled;

                        // ── CHECKPOINT DIFF FLASH via PrintToCenter (layouts 2-4, plain text) ──
                        if (playerTimer.HudLayout != 1 && hudEnabled && !string.IsNullOrEmpty(playerTimer.CheckpointDiffText)
                                       && DateTime.Now < playerTimer.CheckpointDiffExpiry)
                        {
                            bool faster      = playerTimer.CheckpointDiffFaster;
                            string timeColor  = faster ? "#4FC3F7" : "#EF5350"; // blue = faster, red = slower
                            string timeArrow  = faster ? "▲" : "▼";
                            string label      = playerTimer.CheckpointFlashPlacement ?? "";
                            string time       = playerTimer.CheckpointFlashTime ?? "";
                            string timeDiff   = playerTimer.CheckpointDiffText ?? "";
                            

                            string speedHtml = "";
                            if (!string.IsNullOrEmpty(playerTimer.CheckpointFlashSpeed) && !string.IsNullOrEmpty(playerTimer.CheckpointFlashSpeedDiff))
                            {
                                string speedDiff = playerTimer.CheckpointFlashSpeedDiff;
                                bool speedFaster = speedDiff.StartsWith("+")
                                                     ? int.TryParse(speedDiff.TrimStart('+'), out int sv) && sv > 0
                                                     : int.TryParse(speedDiff, out int sv2) && sv2 > 0;
                                string speedColor = speedFaster ? "#66BB6A" : "#FFA726"; // green = faster, orange = slower
                                string speedArrow = speedFaster ? "▲" : "▼";
                                speedHtml = $"<br><font color='{speedColor}'>{playerTimer.CheckpointFlashSpeed} u/s  {speedArrow} {speedDiff}</font>";
                            }

                            player.PrintToCenter(
                                $"<font class='fontSize-s stratum-bold-italic' color='gray'>{label}  ·  {time}</font><br>" +
                                $"<font class='fontSize-m stratum-bold-italic' color='{timeColor}'>{timeArrow} {timeDiff}s</font>" +
                                speedHtml
                            );
                        }
                        
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
            return playerTimer.HudLayout switch {
                2 => GetHudContentLayout2(playerTimer, player),
                3 => GetHudContentLayout3(playerTimer, player),
                4 => GetHudContentLayout4(playerTimer, player),
                _ => GetHudContentLayout1(playerTimer, player)
            };
        }


        // HUD 1 — Compact, CP flash enabled colorcoded.

        private string GetHudContentLayout1(PlayerTimerInfo playerTimer, CCSPlayerController player)
        {
            bool isTimerRunning      = playerTimer.IsTimerRunning;
            bool isBonusTimerRunning = playerTimer.IsBonusTimerRunning;
            int timerTicks           = playerTimer.TimerTicks;
            PlayerButtons? playerButtons = player.Buttons;
            Vector_t playerSpeed     = player.PlayerPawn!.Value!.AbsVelocity.ToVector_t();
            bool keyEnabled          = !playerTimer.HideKeys && !playerTimer.IsReplaying && keysOverlayEnabled;
            bool hudEnabled          = !playerTimer.HideTimerHud && hudOverlayEnabled;

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
            if (playerTime.Length > 0) playerTime = playerTime.Substring(0, playerTime.Length - 1);
            if (playerTime.IndexOf(':') == 1) playerTime = "0" + playerTime;
            string playerBonusTime = Utils.FormatTime(playerTimer.BonusTimerTicks);
            if (playerBonusTime.Length > 0) playerBonusTime = playerBonusTime.Substring(0, playerBonusTime.Length - 1);
            if (playerBonusTime.IndexOf(':') == 1) playerBonusTime = "0" + playerBonusTime;

            // ── CHECKPOINT DIFF FLASH (HTML, colored text) ───────────────────
            string flashLine = "";
            if (hudEnabled && !string.IsNullOrEmpty(playerTimer.CheckpointDiffText)
                           && DateTime.Now < playerTimer.CheckpointDiffExpiry)
            {
                bool faster     = playerTimer.CheckpointDiffFaster;
                string timeColor  = faster ? "#5a97fa" : "#fa5a5a";
                string arrow      = faster ? "▲" : "▼";
                string label      = playerTimer.CheckpointFlashPlacement ?? "";
                string time       = playerTimer.CheckpointFlashTime ?? "";

                flashLine =
                    $"<div align='center'>" +
                    $"<font color='white'>{label}  ·  {time}</font><br>" +
                    $"<font color='{timeColor}'>{arrow} {playerTimer.CheckpointDiffText}s</font><br>";

                if (!string.IsNullOrEmpty(playerTimer.CheckpointFlashSpeed) && !string.IsNullOrEmpty(playerTimer.CheckpointFlashSpeedDiff))
                {
                    string speedDiff   = playerTimer.CheckpointFlashSpeedDiff;
                    bool   speedFaster = speedDiff.StartsWith("+")
                                         ? int.TryParse(speedDiff.TrimStart('+'), out int sv) && sv > 0
                                         : int.TryParse(speedDiff, out int sv2) && sv2 > 0;
                    string speedColor  = speedFaster ? "#3b992c" : "#9c5125";
                    string speedArrow  = speedFaster ? "▲" : "▼";
                    flashLine +=
                        $"<font color='white'>{playerTimer.CheckpointFlashSpeed} u/s  </font>" +
                        $"<font color='{speedColor}'>{speedArrow} {speedDiff}</font><br>";
                }
                flashLine += "</div>";
            }

            string timerLine =
                isBonusTimerRunning
                    ? $" <font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'>Bonus #{playerTimer.BonusStage}</font> " +
                        $"<font class='fontSize-xl horizontal-center' color='{primaryHUDcolor}'>{playerBonusTime}</font> <br>"
                    : isTimerRunning
                        ? $" <font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'></font>" +
                            $"<font class='fontSize-xl horizontal-center' color='{primaryHUDcolor}'>{playerTime}</font><br>"
                        : playerTimer.IsReplaying
                            ? $" <font class='horizontal-center' color='red'>◉ REPLAY {Utils.FormatTime(playerReplays[player.Slot].CurrentPlaybackFrame)}</font> <br>"
                            : "";

            string veloLine =
                $" {(playerTimer.IsTester ? playerTimer.TesterSmolGif : "")}" +
                $"<font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'>   Speed </font> " +
                $"{(playerTimer.IsReplaying ? "<font class=''" : "<font class='fontSize-l horizontal-center'")} color=' {playerVelColor}'>{formattedPlayerVel}</font> ";

            string syncLine =
                $"<font class='fontSize-l horizontal-center' color='{secondaryHUDcolor}'>| {playerTimer.Sync:F2}%</font> " +
                $"<font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'> Sync</font> <br>";

            string infoLine =
                playerTimer.CurrentZoneInfo.InBonusStartZone
                ? GetBonusInfoLine(playerTimer)
                : GetMainMapInfoLine(playerTimer) +
                $"{((playerTimer.CurrentMapStage != 0 && useStageTriggers == true) ? $" <font color='gray' class='fontSize-s stratum-bold-italic'> | Stage: {playerTimer.CurrentMapStage}/{stageTriggerCount}</font>" : "")} " +
                $"<font class='fontSize-s stratum-bold-italic' color='gray'>| ({formattedPlayerPre})|</font>{(playerTimer.IsTester ? playerTimer.TesterSmolGif : "")} " +
                $"<font color='gray' class='fontSize-s stratum-bold-italic'>{GetPlayerPlacement(player)}</font>";

            string keysLineNoHtml = $"{(hudEnabled ? "<br>" : "")}<font class='fontSize-ml stratum-light-mono' color='{tertiaryHUDcolor}'>" +
                $"{((playerButtons & PlayerButtons.Moveleft)  != 0 ? "A" : "_")} " +
                $"{((playerButtons & PlayerButtons.Forward)   != 0 ? "W" : "_")} " +
                $"{((playerButtons & PlayerButtons.Moveright) != 0 ? "D" : "_")} " +
                $"{((playerButtons & PlayerButtons.Back)      != 0 ? "S" : "_")} " +
                $"{((playerButtons & PlayerButtons.Jump)      != 0 || playerTimer.MovementService!.LegacyJump.OldJumpPressed ? "J" : "_")} " +
                $"{((playerButtons & PlayerButtons.Duck)      != 0 ? "C" : "_")}";

            return (hudEnabled
                        ? flashLine +
                          (VelocityHudEnabled ? veloLine : "") +
                          (StrafeHudEnabled && !playerTimer.IsReplaying ? syncLine : "") +
                          timerLine +
                          infoLine
                        : "") +
                   (keyEnabled && !playerTimer.IsReplaying ? keysLineNoHtml : "");
        }


        // Hud 2 — Simple Close to Original CP flash white. 

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
            if (playerTime.Length > 0) playerTime = playerTime.Substring(0, playerTime.Length - 1); // trim to centiseconds
            if (playerTime.IndexOf(':') == 1) playerTime = "0" + playerTime; // 0:44.71 → 00:44.71
            string playerBonusTime = Utils.FormatTime(playerTimer.BonusTimerTicks);
            if (playerBonusTime.Length > 0) playerBonusTime = playerBonusTime.Substring(0, playerBonusTime.Length - 1);
            if (playerBonusTime.IndexOf(':') == 1) playerBonusTime = "0" + playerBonusTime;

            string timerLine =
                isBonusTimerRunning
                    ? $" <font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'>Bonus #{playerTimer.BonusStage}</font> " +
                        $"<font class='fontSize-xl horizontal-center' color='{primaryHUDcolor}'>{playerBonusTime}</font> <br>"
                    : isTimerRunning
                        ? $" <font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'></font>" +
                            $"<font class='fontSize-xl horizontal-center' color='{primaryHUDcolor}'>{playerTime}</font><br>"
                        : playerTimer.IsReplaying
                            ? $" <font class='horizontal-center' color='red'>◉ REPLAY {Utils.FormatTime(playerReplays[player.Slot].CurrentPlaybackFrame)}</font> <br>"
                            : "";

            string veloLine =
                $" {(playerTimer.IsTester ? playerTimer.TesterSmolGif : "")}" +
                
                $"<font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'>   Speed </font> " +
                $"{(playerTimer.IsReplaying ? "<font class=''" : "<font class='fontSize-l horizontal-center'")} color=' {playerVelColor}'>{formattedPlayerVel}</font> " ;
                

            string syncLine =
                $"<font class='fontSize-l horizontal-center' color='{secondaryHUDcolor}'>| {playerTimer.Sync:F2}%</font> " +
                $"<font class='fontSize-s stratum-bold-italic' color='{tertiaryHUDcolor}'> Sync</font> <br>";

            string infoLine = 
                playerTimer.CurrentZoneInfo.InBonusStartZone
                ? GetBonusInfoLine(playerTimer)
                : GetMainMapInfoLine(playerTimer) + 
                $"{((playerTimer.CurrentMapStage != 0 && useStageTriggers == true) ? $" <font color='gray' class='fontSize-s stratum-bold-italic'> | Stage: {playerTimer.CurrentMapStage}/{stageTriggerCount}</font>" : "")} " +
                $"<font class='fontSize-s stratum-bold-italic' color='gray'>| ({formattedPlayerPre})|</font>{(playerTimer.IsTester ? playerTimer.TesterSmolGif : "")} " + 
                $"<font color='gray' class='fontSize-s stratum-bold-italic'>{GetPlayerPlacement(player)}</font>";

            string keysLineNoHtml = $"{(hudEnabled ? "<br>" : "")}<font class='fontSize-ml stratum-light-mono' color='{tertiaryHUDcolor}'>" +
                $"{((playerButtons & PlayerButtons.Moveleft)  != 0 ? "A" : "_")} " +
                $"{((playerButtons & PlayerButtons.Forward)   != 0 ? "W" : "_")} " +
                $"{((playerButtons & PlayerButtons.Moveright) != 0 ? "D" : "_")} " +
                $"{((playerButtons & PlayerButtons.Back)      != 0 ? "S" : "_")} " +
                $"{((playerButtons & PlayerButtons.Jump)      != 0 || playerTimer.MovementService!.LegacyJump.OldJumpPressed ? "J" : "_")} " +
                $"{((playerButtons & PlayerButtons.Duck)      != 0 ? "C" : "_")}";

            return (hudEnabled
                        ? (VelocityHudEnabled ? veloLine : "") +
                          (StrafeHudEnabled && !playerTimer.IsReplaying ? syncLine : "") +
                          timerLine +
                          infoLine
                        : "") +
                   (keyEnabled && !playerTimer.IsReplaying ? keysLineNoHtml : "");
        }


        // LAYOUT 3 — experimental sprite-based layout

        private const string SpriteBase = "https://raw.githubusercontent.com/Jimmarn/poor-sharptimer/refs/heads/main/";

        private static string LcdDigits(string number, string folder, string suffix, int height)
        {
            var sb = new System.Text.StringBuilder();
            string baseUrl = $"{SpriteBase}Numerical%20Sprites/{folder}/";
            foreach (char c in number)
            {
                if (c == ' ') continue;
                string file = c == '.' ? $"dot-{suffix}.png" : $"{c}-{suffix}.png";
                sb.Append($"<img src='{baseUrl}{file}' height='{height}'>");
            }
            return sb.ToString();
        }

        private static string TimerDigits(string time, int height)
        {
            var sb = new System.Text.StringBuilder();
            string baseUrl = $"{SpriteBase}Numerical%20Sprites/TimerDigits/";
            foreach (char c in time)
            {
                string file = c switch {
                    ':' => "colon-oswald.png",
                    '.' => "dot-oswald.png",
                    _   => $"{c}-oswald.png"
                };
                sb.Append($"<img src='{baseUrl}{file}' height='{height}'>");
            }
            return sb.ToString();
        }

        private static string KeyIcon(string key, bool pressed, int height)
        {
            string prefix = pressed ? "KeyPress" : "Key";
            return $"<img src='{SpriteBase}KeyIcons/{prefix}-{key}.png' height='{height}'>";
        }

        private static string OrdinalSuffix(string rankStr)
        {
            if (!int.TryParse(rankStr, out int n) || n <= 0) return rankStr ?? "";
            return (n % 100) switch {
                11 or 12 or 13 => $"{n}th",
                _ => (n % 10) switch {
                    1 => $"{n}st",
                    2 => $"{n}nd",
                    3 => $"{n}rd",
                    _ => $"{n}th"
                }
            };
        }

        private string GetHudContentLayout3(PlayerTimerInfo playerTimer, CCSPlayerController player)
        {
            bool isTimerRunning = playerTimer.IsTimerRunning;
            bool isBonusRunning = playerTimer.IsBonusTimerRunning;
            PlayerButtons? btns = player.Buttons;
            Vector_t vel        = player.PlayerPawn!.Value!.AbsVelocity.ToVector_t();
            bool keyEnabled     = !playerTimer.HideKeys && !playerTimer.IsReplaying && keysOverlayEnabled;
            bool hudEnabled     = !playerTimer.HideTimerHud && hudOverlayEnabled;

            if (!hudEnabled && !keyEnabled) return "";

            int iVel    = (int)Math.Round(use2DSpeed ? vel.Length2D() : vel.Length());
            string fmtVel = iVel.ToString("0000");  // always 4 digits: 0260
            string fmtPre = ((int)Math.Round(Utils.ParseVector_t(playerTimer.PreSpeed ?? "0 0 0").Length2D())).ToString("0000");
            // Timer: fixed width by always zero-padding minutes to 2 digits → "00:04.521"
            // Empty string when not running (not replaying, not bonus) — same as original behavior
            string playerTime = "";
            if (isTimerRunning)
                playerTime = PadTimerFixed(Utils.FormatTime(playerTimer.TimerTicks));
            else if (isBonusRunning)
                playerTime = PadTimerFixed(Utils.FormatTime(playerTimer.BonusTimerTicks));
            else if (playerTimer.IsReplaying)
                playerTime = PadTimerFixed(Utils.FormatTime(playerReplays[player.Slot].CurrentPlaybackFrame));
            // sync always 5 chars: 00.00 – 99.99 (cap at 99.99 to avoid 6-char 100.00)
            string syncStr = playerTimer.Sync >= 99.995 ? "99.99" : playerTimer.Sync.ToString("00.00");

            var sb = new System.Text.StringBuilder();



            // ── MAIN HUD — row by row, CS2 doesn't support multi-column tables ──

            if (hudEnabled)
            {
                // Row 1: left info line | speed sprites | right key row 1 (W)
                bool kW = keyEnabled && !playerTimer.IsReplaying && (btns & PlayerButtons.Forward)   != 0;
                bool kA = keyEnabled && !playerTimer.IsReplaying && (btns & PlayerButtons.Moveleft)  != 0;
                bool kS = keyEnabled && !playerTimer.IsReplaying && (btns & PlayerButtons.Back)      != 0;
                bool kD = keyEnabled && !playerTimer.IsReplaying && (btns & PlayerButtons.Moveright) != 0;
                bool kJ = keyEnabled && !playerTimer.IsReplaying && ((btns & PlayerButtons.Jump) != 0 || playerTimer.MovementService!.LegacyJump.OldJumpPressed);
                bool kC = keyEnabled && !playerTimer.IsReplaying && (btns & PlayerButtons.Duck)      != 0;

                int kDir = 10;
                int kCJ  = 7;

                string placement = OrdinalSuffix(playerTimer.CachedMapPlacement ?? "");
                string pb        = (playerTimer.CachedPB ?? "").Trim();
                string stageStr  = (useStageTriggers && playerTimer.CurrentMapStage != 0)
                                   ? $"{playerTimer.CurrentMapStage}/{stageTriggerCount}" : "";
                string tierStr   = (MapTierHudEnabled && currentMapTier != null) ? $"Tier {currentMapTier}" : "";

                // Line 1: [mode]  [speed sprites]  [W key]
                sb.Append($"<font class='fontSize-s stratum-bold-italic' color='gray'>{playerTimer.Mode}</font>");
                if (VelocityHudEnabled)
                    sb.Append($"\u00A0\u00A0{LcdDigits(fmtVel, "light-blue", "lblue", 10)}");
                if (keyEnabled && !playerTimer.IsReplaying)
                    sb.Append($"\u00A0\u00A0{KeyIcon("Up", kW, kDir)}");
                sb.Append("<br>");

                // Line 2: [placement]  [sync sprites]  [A S D keys]
                if (!string.IsNullOrEmpty(placement))
                    sb.Append($"<font class='fontSize-m stratum-bold-italic' color='white'>{placement}</font>");
                if (StrafeHudEnabled && !playerTimer.IsReplaying)
                    sb.Append($"\u00A0\u00A0{LcdDigits(syncStr, "Orange", "orange", 7)}");
                if (keyEnabled && !playerTimer.IsReplaying)
                    sb.Append($"\u00A0\u00A0{KeyIcon("Left", kA, kDir)}{KeyIcon("Down", kS, kDir)}{KeyIcon("Right", kD, kDir)}");
                sb.Append("<br>");

                // Line 3: [PB time]  [timer sprites]  [C  J keys]
                if (!string.IsNullOrEmpty(pb))
                    sb.Append($"<font class='fontSize-s stratum-light-mono' color='gray'>{pb}</font>");
                if (!string.IsNullOrEmpty(playerTime))
                {
                    sb.Append($"\u00A0\u00A0");
                    if (isBonusRunning)
                        sb.Append($"<font class='fontSize-s stratum-bold-italic' color='gray'>B{playerTimer.BonusStage}\u00A0</font>");
                    else if (playerTimer.IsReplaying)
                        sb.Append($"<font class='fontSize-s stratum-bold-italic' color='red'>◉\u00A0</font>");
                    sb.Append(TimerDigits(playerTime, 16));
                }
                if (keyEnabled && !playerTimer.IsReplaying)
                    sb.Append($"\u00A0\u00A0{KeyIcon("C", kC, kCJ)}\u00A0\u00A0{KeyIcon("J", kJ, kCJ)}");
                sb.Append("<br>");

                // Line 4: [stage | tier | rank icon]  [pre-speed]
                if (!string.IsNullOrEmpty(stageStr) || !string.IsNullOrEmpty(tierStr) || (RankIconsEnabled && !string.IsNullOrEmpty(playerTimer.RankHUDIcon)))
                {
                    if (!string.IsNullOrEmpty(stageStr))
                        sb.Append($"<font class='fontSize-s stratum-bold-italic' color='gray'>{stageStr}</font>");
                    if (RankIconsEnabled && !string.IsNullOrEmpty(playerTimer.RankHUDIcon))
                        sb.Append($"\u00A0<img src='{playerTimer.RankHUDIcon}' height='12'>");
                    if (!string.IsNullOrEmpty(tierStr))
                        sb.Append($"<font class='fontSize-s stratum-bold-italic' color='gray'>\u00A0{tierStr}</font>");
                    if (fmtPre != "0000" && !string.IsNullOrEmpty(fmtPre))
                        sb.Append($"\u00A0\u00A0{LcdDigits(fmtPre, "Gray", "gray", 7)}");
                    sb.Append("<br>");
                }
            }

            return sb.ToString();
        }

       
        // HUD 4 — original layout from P-ST
       
        private string GetHudContentLayout4(PlayerTimerInfo playerTimer, CCSPlayerController player)
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

        // Helpers shared by both layouts

        /// <summary>Pads minutes to 2 digits and trims to centiseconds (MM:SS.cc — always 8 chars).</summary>
        private static string PadTimerFixed(string time)
        {
            int colonIdx = time.IndexOf(':');
            if (colonIdx == 1) time = "0" + time;   // "4:52.123" → "04:52.123"
            // drop last digit: "04:52.123" → "04:52.12"
            if (time.Length > 0) time = time.Substring(0, time.Length - 1);
            return time;
        }

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
