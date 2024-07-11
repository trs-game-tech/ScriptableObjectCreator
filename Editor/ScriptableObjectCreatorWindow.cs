using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class ScriptableObjectCreatorWindow : EditorWindow
{
    private const string MenuName = "Assets/Create/ScriptableObject (using Creator)";
    private const string SearchTextControlName = nameof(ScriptableObjectCreatorWindow) + ".Search";
    private static readonly char[] SearchTextSplitSeparator = { ' ' };
    private static readonly GUILayoutOption[] ClearButtonGUIOptions = { GUILayout.ExpandWidth(false) };

    private static readonly string[] ExcludeAssemblyPrefixes =
    {
        "Unity.",
        "UnityEngine.",
        "UnityEditor.",
    };

    private static readonly Type[] ExcludeSubclasses = new Type[]
    {
        typeof(Editor),
        typeof(EditorWindow),
        Type.GetType("UnityEngine.StateMachineBehaviour,UnityEngine.AnimationModule"),
        Type.GetType("UnityEngine.Timeline.Marker,Unity.Timeline"),
    }.Where(t => t != null).ToArray();

    private static Type[] s_types;

    private Vector2 _scroll;
    private string _searchText;
    private string _destinationDirectory;
    private List<Type> _matchTypes = new List<Type>();
    private string _focusTo;

    [MenuItem(MenuName)]
    private static void Launch()
    {
        var selectedAsset = GetSelectedAsset();
        var destinationDirectory = GetAssetDirectoryPath(selectedAsset);
        if (selectedAsset is MonoScript monoScript)
        {
            CreateScriptableObject(monoScript.GetClass(), destinationDirectory);
            return;
        }

        var window = CreateInstance<ScriptableObjectCreatorWindow>();
        window.titleContent = new GUIContent("ScriptableObjectCreator");
        window._destinationDirectory = destinationDirectory;
        window.ShowModal();
    }

    private void OnEnable()
    {
        if (s_types == null)
        {
            s_types = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .Where(IsTargetScriptableObject)
                .OrderBy(type => type.FullName)
                .ToArray();
        }

        ApplySearch();
        _focusTo = SearchTextControlName;
    }

    private void OnGUI()
    {
        string nextFocusControlName = null;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.SetNextControlName(SearchTextControlName);
            var text = EditorGUILayout.TextField(_searchText);
            if (text != _searchText)
            {
                _searchText = text;
                ApplySearch();
            }

            if (GUILayout.Button("clear", ClearButtonGUIOptions))
            {
                _searchText = null;

                GUI.FocusControl("");
                nextFocusControlName = SearchTextControlName;

                ApplySearch();
            }
        }

        using (var scrollScope = new EditorGUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scrollScope.scrollPosition;

            if (_matchTypes.Count > 0)
            {
                foreach (var type in _matchTypes)
                {
                    if (GUILayout.Button(type.FullName))
                    {
                        Close();
                        CreateScriptableObject(type, _destinationDirectory);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Not found.");
            }
        }

        if (!string.IsNullOrEmpty(_focusTo))
        {
            GUI.FocusControl(_focusTo);
            _focusTo = null;
        }

        if (!string.IsNullOrEmpty(nextFocusControlName))
        {
            _focusTo = nextFocusControlName;
            Repaint();
        }
    }

    private void ApplySearch()
    {
        _matchTypes.Clear();

        var tokens = ParseTokens(_searchText);
        if (tokens.Length == 0)
        {
            _matchTypes.AddRange(s_types);
            return;
        }

        foreach (var type in s_types)
        {
            if (IsMatch(type, tokens))
            {
                _matchTypes.Add(type);
            }
        }
    }

    private static string[] ParseTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        return text.Split(SearchTextSplitSeparator)
            .Select(s => s.Trim())
            .Where(t => t.Length > 0)
            .ToArray();
    }

    private static bool IsMatch(Type type, string[] tokens)
    {
        var fullName = type.FullName;
        if (fullName == null)
            return false;

        foreach (var token in tokens)
        {
            if (!fullName.Contains(token, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static void CreateScriptableObject(Type type, string destinationDirectory)
    {
        if (string.IsNullOrEmpty(destinationDirectory))
            destinationDirectory = "Assets";

        var asset = CreateInstance(type);
        var assetPath = string.Format("{0}/{1}.asset", destinationDirectory, type.Name);
        ProjectWindowUtil.CreateAsset(asset, assetPath);
    }

    private static UnityEngine.Object GetSelectedAsset()
    {
        return Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets).FirstOrDefault();
    }

    private static string GetAssetDirectoryPath(UnityEngine.Object asset)
    {
        if (asset == null)
            return null;

        var assetPath = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(assetPath))
            return null;

        if (AssetDatabase.IsValidFolder(assetPath))
            return assetPath;

        var lastSeparatorIndex = assetPath.LastIndexOf('/');
        if (lastSeparatorIndex != -1)
        {
            var path = assetPath.Substring(0, lastSeparatorIndex);
            if (AssetDatabase.IsValidFolder(path))
                return path;
        }
        return null;
    }

    private static bool IsTargetScriptableObject(Type type)
    {
        if (!type.IsPublic && !type.IsNestedPublic)
            return false;

        if (type.IsAbstract || type.IsGenericType)
            return false;

        var assemblyFullname = type.Assembly.FullName;
        foreach (var excludeAssemblyPrefix in ExcludeAssemblyPrefixes)
        {
            if (assemblyFullname.StartsWith(excludeAssemblyPrefix, StringComparison.Ordinal))
                return false;
        }

        foreach (var excludeSubclass in ExcludeSubclasses)
        {
            if (type.IsSubclassOf(excludeSubclass))
                return false;
        }
        return true;
    }
}
