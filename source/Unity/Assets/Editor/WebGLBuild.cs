// ポートフォリオ用：コマンドラインから WebGL ビルドを作るためのスクリプト
// Unity.exe -batchmode -quit -projectPath <path> -executeMethod WebGLBuild.Build -buildOut <dir> -scene <path>
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLBuild
{
    static string Arg(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return fallback;
    }

    public static void Build()
    {
        string outDir = Arg("-buildOut", "Builds/WebGL");
        string scene = Arg("-scene", "Assets/Scenes/MainGame.unity");

        // 大学のサーバーは圧縮ファイル用の設定ができないので、ブラウザ側で解凍できる形にする
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.runInBackground = false;

        var options = new BuildPlayerOptions
        {
            scenes = scene.Split(';'),
            locationPathName = outDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[WebGLBuild] result={report.summary.result} size={report.summary.totalSize} errors={report.summary.totalErrors}");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
