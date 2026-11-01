namespace Before.Presentation
{
    /// <summary>Exact public strings from narrative-presentation.md. Do not improvise synonyms.</summary>
    public static class BeforeTextCatalog
    {
        public const string BOOT_WAIT_1 = "SYS// 外部視覚リンクを待機";
        public const string BOOT_WAIT_2 = "手をフレーム内へ";
        public const string CAMERA_DENIED_1 = "SYS// カメラに接続できません";
        public const string CAMERA_DENIED_2 = "接続を確認してください";
        public const string LOW_LIGHT_1 = "SYS// 照度が不足しています";
        public const string LOW_LIGHT_2 = "手元を明るくしてください";
        public const string LINKING_1 = "SYS// エネルギー経路を接続中";
        public const string LINK_OK_1 = "SYS// 接続：安定";
        public const string EDGE_WARN_1 = "SYS// 接続範囲から外れています";
        public const string EDGE_WARN_2 = "手を中央へ";
        public const string SIGNAL_LOST_1 = "SYS// エネルギー供給が途絶しました";
        public const string SIGNAL_LOST_2 = "手をフレーム内へ";
        // D-092 compound (Pose + Motion) teaching: shape first, then move. No technical terms in public text.
        public const string TEACH_BEGIN_1 = "SYS// 合図の形を記録";
        public const string TEACH_BEGIN_2 = "SPACEを押したまま手を止める";
        public const string POSE_CONFIRM_1 = "SYS// 形を確認";
        public const string POSE_CONFIRM_2 = "そのまま手を動かす";
        public const string SAMPLE_ONE_1 = "SYS// 合図を記録：1/3";
        public const string SAMPLE_ONE_2 = "同じ組合せをもう一度";
        public const string SAMPLE_SHORT_1 = "SYS// 動作が短すぎます";
        public const string SAMPLE_SHORT_2 = "もう一度送信してください";
        public const string SAMPLE_CONFLICT_1 = "SYS// 合図が安定しません";
        public const string SAMPLE_CONFLICT_2 = "同じ組合せをもう一度";
        public const string SIGNAL_OVERLAP_1 = "SYS// 既存の合図と区別できません";
        public const string SIGNAL_OVERLAP_2 = "別の組合せを作ってください";
        public const string LEARNED_1 = "SYS// 対応を記憶しました";
        // D-092: the presenter appends the running "N/3" count to line 1
        public const string TEACH_RECORD_1 = "SYS// 合図を記録";
        public const string TEACH_RECORD_2 = "同じ組合せをもう一度";
        // D-096 per-gesture announcements (why NUL needs the sign) — BeforeTextCatalog is the single source
        public const string JUMP_ANNOUNCE_1 = "SYS// NULはこの先へ進めません";
        public const string JUMP_ANNOUNCE_2 = "越えるための合図を教えてください";
        public const string STOP_ANNOUNCE_1 = "SYS// 前方に周期障害を検出";
        public const string STOP_ANNOUNCE_2 = "NULに待つ合図を教えてください";
        public const string DIRECTION_ANNOUNCE_1 = "SYS// NULには経路全体が見えません";
        public const string DIRECTION_ANNOUNCE_2 = "進む方向を指で示してください";
        // D-095 static Stop sign teaching (no motion instruction)
        public const string STOP_TEACH_1 = "SYS// 待つ合図の形を記録";
        public const string STOP_TEACH_2 = "SPACEを押したまま形を保つ";
        // D-096 remaining-count between samples (never shown together with the confirmed line)
        public const string REMAIN_TWO = "同じ組合せをあと2回";
        public const string REMAIN_ONE = "同じ組合せをあと1回";
        public const string QUEUE_FULL_1 = "SYS// 保持できる合図は3つまでです";
        public const string PLAN_WAIT_1 = "SYS// 経路を選択してください";
        public const string PLAN_WAIT_2 = "左右どちらかへ手を動かす";
        public const string PLAN_FINAL_1 = "SYS// 経路と合図を送信してください";
        public const string PLAN_FINAL_2 = "保持領域：3";
        public const string REWIND_1 = "SYS// 安全地点へ復元中";
        public const string INDEPENDENT_1 = "SYS// 独立電源を確認";
        public const string INDEPENDENT_2 = "外部供給を終了します";
        public const string COMPLETE_1 = "SYS// 接続記録を終了";
        public const string PAUSED_1 = "SYS// 実行を一時停止";
        public const string END_RESET_1 = "SYS// R 長押し：再接続";
        public const string EXIT_CONFIRM_1 = "SYS// 実行を終了しますか";
        public const string EXIT_CONFIRM_2 = "ENTER：終了  ESC：戻る";
    }
}
