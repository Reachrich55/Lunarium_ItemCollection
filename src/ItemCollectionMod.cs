using System;
using System.IO;
using System.Reflection;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LunariumItemCollectionMod;

public sealed class ItemCollectionMod : MelonMod
{
    private const string DatabaseResourceName = "LunariumItemCollectionMod.collectibles.json";
    private const float RefreshIntervalSeconds = 1.0f;

    private MapDatabase? _database;
    private MarkerOverlay? _overlay;
    private bool _enabled;
    private bool _refreshRequested;
    private float _nextRefreshTime;
    private float _nextErrorLogTime;

    public override void OnInitializeMelon()
    {
        try
        {
            _database = LoadDatabase();
            _overlay = new MarkerOverlay(_database);
            Lunarium.SaveManager.OnSaveLoadComplete += OnSaveLoaded;
            LoggerInstance.Msg($"已载入 {_database.Worlds.Count} 个世界的收集数据。按 F8 启用/停用。");
        }
        catch (Exception exception)
        {
            LoggerInstance.Error("初始化失败：" + exception);
        }
    }

    public override void OnUpdate()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
        {
            Toggle();
        }

        if (!_enabled || _database == null || _overlay == null)
        {
            return;
        }

        if (_refreshRequested || Time.unscaledTime >= _nextRefreshTime)
        {
            RefreshOverlay();
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        _refreshRequested = true;
    }

    public override void OnDeinitializeMelon()
    {
        Lunarium.SaveManager.OnSaveLoadComplete -= OnSaveLoaded;
        _overlay?.Clear();
    }

    private void Toggle()
    {
        _enabled = !_enabled;
        if (_enabled)
        {
            _refreshRequested = true;
            LoggerInstance.Msg("收集标记已启用。打开游戏地图即可查看。");
        }
        else
        {
            _overlay?.SetVisible(false);
            LoggerInstance.Msg("收集标记已停用。");
        }
    }

    private void RefreshOverlay()
    {
        _refreshRequested = false;
        _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
        try
        {
            if (Lunarium.SaveManager.CurrentData == null)
            {
                return;
            }

            _overlay!.Refresh(Lunarium.SaveManager.CurrentData);
            _overlay.SetVisible(true);
        }
        catch (Exception exception)
        {
            if (Time.unscaledTime >= _nextErrorLogTime)
            {
                LoggerInstance.Error("刷新地图标记失败：" + exception);
                _nextErrorLogTime = Time.unscaledTime + 10f;
            }
        }
    }

    private void OnSaveLoaded()
    {
        _refreshRequested = true;
    }

    private static MapDatabase LoadDatabase()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using Stream? stream = assembly.GetManifestResourceStream(DatabaseResourceName);
        if (stream == null)
        {
            throw new InvalidOperationException("内置收集数据不存在：" + DatabaseResourceName);
        }

        using var reader = new StreamReader(stream);
        MapDatabase? database = JsonConvert.DeserializeObject<MapDatabase>(reader.ReadToEnd());
        if (database == null || database.Worlds.Count == 0)
        {
            throw new InvalidDataException("内置收集数据为空或格式无效。");
        }

        return database;
    }
}
