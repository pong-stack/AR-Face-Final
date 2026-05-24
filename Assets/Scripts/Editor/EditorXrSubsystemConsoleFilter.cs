#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Windows Editor webcam workflow: XR plug-ins often surface benign subsystem probe logs.
/// This filters only LogType.Log / Warning matching known subsystem identifiers while Edit Mode diagnostics stay intact.
/// Player builds omit this script (Editor-folder); mobile AR stacks are unaffected.
/// </summary>
internal static class EditorXrSubsystemConsoleFilter
{
    static ILogHandler _savedChain;
    static bool _handlerInstalled;

    [InitializeOnLoadMethod]
    static void HookPlayModeTransitions()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }
    static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
            Install();
        else if (change is PlayModeStateChange.ExitingPlayMode or PlayModeStateChange.EnteredEditMode)
            TearDown();
    }

    static void Install()
    {
        if (_handlerInstalled || !Application.isEditor)
            return;

        _savedChain = Debug.unityLogger.logHandler;
        Debug.unityLogger.logHandler = new XrSubsystemLogFilter(_savedChain);
        _handlerInstalled = true;
    }

    static void TearDown()
    {
        if (!_handlerInstalled)
            return;

        Debug.unityLogger.logHandler = _savedChain ?? Debug.unityLogger.logHandler;
        _savedChain = null;
        _handlerInstalled = false;
    }

    sealed class XrSubsystemLogFilter : ILogHandler
    {
        readonly ILogHandler _forward;

        public XrSubsystemLogFilter(ILogHandler forward) => _forward = forward ?? throw new ArgumentNullException(nameof(forward));

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if (TrySuppressHarmlessSubsystemProbe(logType, format, args))
                return;

            _forward.LogFormat(logType, context, format, args);
        }

        static bool TrySuppressHarmlessSubsystemProbe(LogType logType, string format, params object[] args)
        {
            if (logType != LogType.Log && logType != LogType.Warning)
                return false;

            if (format == null)
                return false;

            try
            {
                string flattened = args is { Length: > 0 }
                    ? string.Format(format, args)
                    : format;

                return ShouldMute(flattened);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        static bool ShouldMute(string flattened)
        {
            if (string.IsNullOrEmpty(flattened))
                return false;

            if (flattened.IndexOf("XRSessionSubsystem", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (flattened.IndexOf("XRInputSubsystem", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (flattened.IndexOf("XRCameraSubsystem", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        public void LogException(Exception exception, UnityEngine.Object context) =>
            _forward.LogException(exception, context);
    }
}
#endif
