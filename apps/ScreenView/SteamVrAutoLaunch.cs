using FloatSoda.OVR;
using FloatSoda.OVR.Exceptions;

namespace FloatSoda.Apps.ScreenView;

/// <summary>
/// SteamVR 起動時にこのサンプルが自動で立ち上がるよう、 SteamVR 側の自動起動フラグを有効化するユーティリティ。
/// </summary>
/// <remarks>
/// <see cref="OpenVR.Applications.SetApplicationAutoLaunch"/> は SteamVR に登録済みのアプリのうち、
/// ダッシュボード オーバーレイ型(screenview.vrmanifest の <c>is_dashboard_overlay</c>: true)のものにのみ許可される。
/// 未登録のキーで呼ぶと <see cref="EVRApplicationError.InvalidApplication"/> になるので、
/// 失敗時はマニフェストを登録してから再度フラグを切り替える。
/// 登録はオーバーレイの本動作とは独立した付加機能なので、失敗してもアプリの実行は継続する(非致命)。
/// </remarks>
internal static class SteamVrAutoLaunch
{
    /// <summary>SteamVR アプリキー。FloatSodaOptions の <c>AppKey</c> と一致させ、IdentifyApplication を成功させる。</summary>
    internal const string AppKey = "floatsoda.apps.screenview";

    private static readonly string ManifestPath = Path.Combine(AppContext.BaseDirectory, "screenview.vrmanifest");

    /// <summary>SteamVR 起動時の自動起動をオンにする。既にオンなら何もしない。</summary>
    internal static void Register()
    {
        if (!File.Exists(ManifestPath))
        {
            Console.Error.WriteLine($"vrmanifest が見つかりません: {ManifestPath}");
            return;
        }

        // OpenVR コンテキストは FloatSodaApp が初期化するので、整うまで数回リトライする。
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (TryEnable(out var alreadyEnabled))
                {
                    var state = alreadyEnabled ? "既に有効" : "有効化しました";
                    Console.WriteLine($"SteamVR 自動起動は{state} (Screen View, app_key={AppKey})");
                    return;
                }

                // アプリが未登録だと SetApplicationAutoLaunch が失敗するため、マニフェストを登録してから再度試す。
                // 同一パスの vrmanifest の再登録は冪等(SteamVR 側で「更新」として扱われる)。
                OVRApplications.AddManifest(ManifestPath);

                if (TryEnable(out _))
                {
                    Console.WriteLine($"SteamVR 自動起動を有効化しました (マニフェスト登録後, app_key={AppKey})");
                    return;
                }
            }
            catch (Exception e)
            {
                if (attempt >= 5)
                {
                    Console.Error.WriteLine($"SteamVR 自動起動の有効化に失敗しました(非致命): {e.Message}");
                    return;
                }
                Console.WriteLine($"SteamVR 自動起動の有効化を再試行します({attempt}/5): {e.Message}");
            }

            Thread.Sleep(2000);
        }
    }

    /// <summary>自動起動フラグをオンにする。呼び出し前の状態が alreadyEnabled に返る。</summary>
    private static bool TryEnable(out bool alreadyEnabled)
    {
        alreadyEnabled = OpenVR.Applications.GetApplicationAutoLaunch(AppKey);
        if (!alreadyEnabled)
        {
            OpenVR.Applications.SetApplicationAutoLaunch(AppKey, true).ThrowIfError();
        }
        return true;
    }
}
