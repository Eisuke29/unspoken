namespace Before.Production
{
    /// <summary>
    /// Single source for participant-visible Production V2 copy.
    /// Diagnostic/operator-only labels are intentionally excluded.
    /// </summary>
    public static class ProductionTextCatalog
    {
        public const string BrandFallback = "UNSPOKEN";
        public const string TitleInstruction = "手を接続範囲に入れてください";
        public const string CameraUnavailable = "SYS// カメラに接続できません";
        public const string CameraUnavailableAction = "接続を確認してください";
        public const string EnergyConnecting = "SYS// エネルギー経路を接続中";
        public const string EnergyConnectingAction = "手をそのまま保ってください";
        public const string LinkEstablished = "SYS// 接続：安定";
        public const string LinkEstablishedAction = "接続を維持してください";
        public const string EnergyLost = "SYS// エネルギー供給が途絶しました";
        public const string EnergyLostAction = "手をフレーム内へ";
        public const string EnergyRecharging = "SYS// エネルギーを再接続中";
        public const string EnergyRechargingAction = "手をそのまま保ってください";

        public const string TeachingUnknown =
            "SYS// NULは、この障害を越える方法を知りません";
        public const string JumpInstruction =
            "手の形を保ち、『越える合図』を動きで教えてください";
        public const string CrouchInstruction =
            "手の形を保ち、『しゃがむ合図』を動きで教えてください";
        public const string StopInstruction =
            "手を動かさず、『待つ合図』の形を教えてください";
        public const string JumpTarget = "ジャンプの合図";
        public const string CrouchTarget = "しゃがむ合図";
        public const string StopTarget = "止まる合図";
        public const string JumpRemembered = "SYS// 「越える合図」を記憶しました";
        public const string CrouchRemembered =
            "SYS// 「身を低くする合図」を記憶しました";
        public const string StopRemembered = "SYS// 「待つ合図」を記憶しました";
        public const string HoldShape = "SPACEを押したまま、合図の形を保つ";
        public const string MoveWithShape = "そのまま手を動かす";
        public const string ReleaseToRecord = "SPACEを離して記録";
        public const string RepairOnce = "合図を調整します　1回";
        public const string ResetHint = "R：この合図を最初から作り直す";

        public const string ForkHeadline = "SYS// 経路が分岐しています";
        public const string ForkInstruction = "進む方向を指してください";
        public const string LeftReceived = "SYS// 左経路を受信";
        public const string LeftProceed = "左へ進みます";
        public const string RightReceived = "SYS// 右経路を受信";
        public const string RightProceed = "右へ進みます";

        public const string CoreHeadline = "SYS// CORE NODEを確認";
        public const string MaintainLink = "接続を維持してください";
        public const string TransferComplete = "SYS// 転送完了";
        public const string EndingComplete = "SYS// 接続記録を終了";
        public const string LinkTerminated = "LINK TERMINATED";
        public const string Restart = "R // RESTART";
        public const string Pause = "SYS// 実行を一時停止";
        public const string Resume = "P // RESUME";
        public const string GameOver = "GAME OVER";
        public const string ZeroLives = "SYS// 復元信号は残っていません";
        public const string Exit = "SYS// 実行を終了しますか";
        public const string ExitAction = "ENTER：終了  ESC：戻る";

        public static string TeachingInstructionFor(ProductionObstacleKind kind)
        {
            switch (kind)
            {
                case ProductionObstacleKind.Jump: return JumpInstruction;
                case ProductionObstacleKind.Crouch: return CrouchInstruction;
                default: return StopInstruction;
            }
        }

        public static string TeachingTargetFor(ProductionObstacleKind kind)
        {
            switch (kind)
            {
                case ProductionObstacleKind.Jump: return JumpTarget;
                case ProductionObstacleKind.Crouch: return CrouchTarget;
                default: return StopTarget;
            }
        }

        public static string TeachingConfirmationFor(ProductionObstacleKind kind)
        {
            switch (kind)
            {
                case ProductionObstacleKind.Jump: return JumpRemembered;
                case ProductionObstacleKind.Crouch: return CrouchRemembered;
                default: return StopRemembered;
            }
        }

        public static string Recorded(int accepted, int required)
            => "記録しました　" + accepted + "/" + required;

        public static string RejectionFor(string reason)
        {
            switch (reason)
            {
                case "early_release": return "離すのが早すぎます";
                case "tracking_lost": return "手を見失いました。手をフレーム内へ";
                case "unstable": return "形が不安定です";
                case "inconsistent": return "前のサンプルと一致しません";
                case "too_far": return "手をカメラに近づけてください";
                case "out_of_frame": return "手を枠の中央に戻してください";
                default: return "サンプルを受け取れませんでした";
            }
        }
    }
}
