using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Belief.EditorTools
{
    /// <summary>WebGL 빌드 진입점. 메뉴(Belief/Build/WebGL)와 배치모드(-executeMethod) 양쪽에서 같은
    /// 경로를 타도록 한 곳에 모아둔다 - 에디터 UI로 뽑은 빌드와 CI로 뽑은 빌드의 설정이 갈리는 것을
    /// 막기 위해서다. 씬 목록은 여기에 하드코딩하지 않고 Build Settings(EditorBuildSettings)에
    /// 등록된 enabled 씬을 그대로 쓴다.
    ///
    /// 압축은 <b>Gzip + Decompression Fallback ON</b>으로 고정한다. Brotli가 더 작지만 서버가
    /// Content-Encoding: br 헤더를 붙여줘야만 동작해서, itch.io·GitHub Pages·로컬 파일 서버 등
    /// 헤더를 못 건드리는 환경에서 흰 화면으로 죽는다. Fallback을 켜두면 헤더가 없어도 JS가
    /// 직접 풀기 때문에 어디에 올려도 일단 뜬다(대신 초기 로딩이 조금 느리다).
    /// 서버 헤더를 직접 제어할 수 있으면 ApplyWebGLSettings의 두 줄을 Brotli/false로 바꾸면 된다.</summary>
    public static class BeliefBuildScript
    {
        const string OutputDirName = "Build/WebGL";

        [MenuItem("Belief/Build/WebGL 설정만 적용", priority = 10)]
        public static void ApplyWebGLSettingsMenu()
        {
            ApplyWebGLSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[BeliefBuild] WebGL Player Settings 적용 완료.");
        }

        [MenuItem("Belief/Build/WebGL 빌드", priority = 11)]
        public static void BuildWebGLMenu() => BuildWebGLDeferred();

        /// <summary>빌드를 <b>지금 이 호출 스택에서 실행하지 않고</b> 다음 에디터 틱으로 미룬다.
        /// BuildPipeline.BuildPlayer는 "플레이어용 스크립트 재컴파일" 단계에서 에디터 메인 루프가
        /// 한 번 더 돌아야 진행되는데, 메인 스레드를 잡고 있는 콜백(예: MCP 커맨드 핸들러, 일부
        /// 커스텀 툴 창) 안에서 동기로 부르면 서로를 기다리며 <b>데드락</b>한다
        /// (증상: Editor.log가 "Recompiling scripts for player build" / bee_backend
        /// ScriptAssembliesAndTypeDB에서 멈추고 CPU가 0으로 떨어진 채 에디터가 응답하지 않음).
        /// 메뉴/외부 툴에서 부를 때는 반드시 이 쪽을 쓴다. 배치모드는 -executeMethod가 이미
        /// 독립된 틱에서 실행되므로 BuildWebGL을 직접 불러도 된다.</summary>
        public static void BuildWebGLDeferred() => EditorApplication.delayCall += BuildWebGL;

        /// <summary>배치모드 진입점:
        /// Unity.exe -quit -batchmode -projectPath &lt;경로&gt; -executeMethod Belief.EditorTools.BeliefBuildScript.BuildWebGL
        /// </summary>
        public static void BuildWebGL()
        {
            ApplyWebGLSettings();

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[BeliefBuild] Build Settings에 활성화된 씬이 없습니다.");
                EditorApplication.Exit(1);
                return;
            }

            string output = Path.Combine(ProjectRoot(), OutputDirName);
            Directory.CreateDirectory(output);

            // 플랫폼이 아직 WebGL이 아니면 먼저 스위치한다(모든 텍스처를 WebGL용으로 재임포트하므로
            // 프로젝트 규모에 따라 수 분 걸릴 수 있다 - 최초 1회만).
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log("[BeliefBuild] 플랫폼을 WebGL로 전환합니다...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    Debug.LogError("[BeliefBuild] WebGL 플랫폼 전환 실패 (WebGL Build Support 모듈이 설치되어 있는지 확인).");
                    EditorApplication.Exit(1);
                    return;
                }
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            Debug.Log($"[BeliefBuild] 빌드 시작 - 씬 {scenes.Length}개 → {output}");
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                RestoreEditorOnlySettings();
            }
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BeliefBuild] 빌드 성공: {summary.totalSize / 1024 / 1024f:F1} MB / "
                          + $"{summary.totalTime.TotalMinutes:F1}분 / 출력 {output}");
            }
            else
            {
                Debug.LogError($"[BeliefBuild] 빌드 실패: {summary.result} (에러 {summary.totalErrors}개)");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static void ApplyWebGLSettings()
        {
            var nbt = NamedBuildTarget.WebGL;

            // 어디에 올려도 뜨게 (클래스 주석 참조)
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;

            // 게임이 1920x1080 기준으로 설계돼 있는데 기본값이 960x600이라 캔버스가 절반 크기로 뜬다.
            PlayerSettings.defaultWebScreenWidth = 1920;
            PlayerSettings.defaultWebScreenHeight = 1080;

            // 기본(Default) 템플릿은 index.html에서 캔버스 CSS 크기를 1920x1080으로 못박아,
            // 브라우저 창이 그보다 작으면(주소창·북마크바 높이만큼은 항상 그렇다) 캔버스가
            // 넘쳐서 위/오른쪽이 잘렸다. BeliefResponsive는 창에 들어가는 가장 큰 16:9
            // 사각형에 맞춘다. 경로는 Assets/WebGLTemplates/BeliefResponsive.
            PlayerSettings.WebGL.template = "PROJECT:BeliefResponsive";

            // 릴리스 기준. 디버깅이 필요하면 FullWithStacktrace로 올리면 되지만 빌드가 크고 느려진다.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;

            // 브라우저 탭을 벗어나도 백그라운드에서 계속 돌게 두면 배터리만 먹는다.
            //
            // ⚠️ 이 값은 플랫폼별이 아니라 프로젝트 전역이라, 꺼 둔 채로 두면 <b>에디터에도</b> 그대로
            // 걸린다 - Unity 창이 비활성이면 프레임이 아예 돌지 않아 LLM 응답을 기다리는 코루틴이
            // 영원히 끝나지 않는다(타임아웃도 프레임을 타므로 함께 멈춘다). 빌드에는 꺼진 값이
            // 들어가야 하므로 여기서 끄되, 빌드가 끝나면 RestoreEditorOnlySettings가 되돌린다.
            runInBackgroundBeforeBuild = PlayerSettings.runInBackground;
            PlayerSettings.runInBackground = false;

            // 리플렉션으로만 참조되는 타입이 없어 Minimal 이상으로 올려도 되지만, 스트리핑 사고는
            // 런타임에만 터져서 잡기 어렵다 - 용량이 문제될 때만 올린다.
            PlayerSettings.SetManagedStrippingLevel(nbt, ManagedStrippingLevel.Minimal);

            PlayerSettings.SetIl2CppCodeGeneration(nbt, Il2CppCodeGeneration.OptimizeSize);
        }

        /// <summary>빌드 산출물에는 이미 구워진 뒤라 되돌려도 결과가 달라지지 않는 설정들 -
        /// 에디터 작업에 방해가 되는 값만 원래대로 돌려놓는다. "설정만 적용" 메뉴에서는 부르지
        /// 않는다(그쪽은 빌드 직전 상태를 눈으로 확인하려는 용도라 그대로 두는 게 맞다).</summary>
        static void RestoreEditorOnlySettings()
        {
            if (PlayerSettings.runInBackground == runInBackgroundBeforeBuild) return;
            PlayerSettings.runInBackground = runInBackgroundBeforeBuild;
            AssetDatabase.SaveAssets();
            Debug.Log($"[BeliefBuild] runInBackground를 {runInBackgroundBeforeBuild}로 되돌렸다(에디터 전용).");
        }

        static bool runInBackgroundBeforeBuild = true;

        static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
    }
}
