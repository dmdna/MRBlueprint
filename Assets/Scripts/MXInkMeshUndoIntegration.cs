using System.Collections.Generic;
using UnityEngine;

public sealed class MXInkMeshUndoIntegration : MonoBehaviour
{
    private static MXInkMeshUndoIntegration _instance;

    private readonly Stack<EditRecord> _undo = new();
    private readonly Stack<EditRecord> _redo = new();

    public static MXInkMeshUndoIntegration Instance
    {
        get
        {
            if (_instance != null)
            {
                return _instance;
            }

            var existing = FindFirstObjectByType<MXInkMeshUndoIntegration>(FindObjectsInactive.Include);
            if (existing != null)
            {
                _instance = existing;
                return _instance;
            }

            var go = new GameObject("MXInkMeshUndoIntegration");
            _instance = go.AddComponent<MXInkMeshUndoIntegration>();
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    public void RecordTopologyEdit(
        MXInkEditableMeshTopology topology,
        MXInkTopologySnapshot before,
        bool topologyWasCreated)
    {
        if (topology == null)
        {
            return;
        }

        _undo.Push(new EditRecord
        {
            Topology = topology,
            Before = before,
            After = topology.CaptureSnapshot(),
            CreatedTopology = topologyWasCreated
        });
        _redo.Clear();
    }

    public static void DiscardTopologyRecords(MXInkEditableMeshTopology topology)
    {
        if (topology == null)
        {
            return;
        }

        var integration = _instance != null
            ? _instance
            : FindFirstObjectByType<MXInkMeshUndoIntegration>(FindObjectsInactive.Include);
        integration?.DiscardRecordsFor(topology);
    }

    public void DiscardRecordsFor(MXInkEditableMeshTopology topology)
    {
        if (topology == null)
        {
            return;
        }

        RemoveRecordsFor(_undo, topology);
        RemoveRecordsFor(_redo, topology);
    }

    public bool TryUndo()
    {
        while (_undo.Count > 0)
        {
            var record = _undo.Pop();
            if (record.Topology == null)
            {
                continue;
            }

            if (record.CreatedTopology)
            {
                if (AssetSelectionManager.Instance != null
                    && AssetSelectionManager.Instance.SelectedAsset == record.Topology.GetComponent<PlaceableAsset>())
                {
                    AssetSelectionManager.Instance.ClearSelection();
                }

                record.Topology.gameObject.SetActive(false);
            }
            else
            {
                record.Topology.RestoreSnapshot(record.Before);
            }

            _redo.Push(record);
            return true;
        }

        return false;
    }

    private static void RemoveRecordsFor(Stack<EditRecord> stack, MXInkEditableMeshTopology topology)
    {
        if (stack == null || stack.Count == 0 || topology == null)
        {
            return;
        }

        var retained = new List<EditRecord>();
        while (stack.Count > 0)
        {
            var record = stack.Pop();
            if (record.Topology != null && record.Topology != topology)
            {
                retained.Add(record);
            }
        }

        for (var i = retained.Count - 1; i >= 0; i--)
        {
            stack.Push(retained[i]);
        }
    }

    public bool TryRedo()
    {
        while (_redo.Count > 0)
        {
            var record = _redo.Pop();
            if (record.Topology == null)
            {
                continue;
            }

            if (record.CreatedTopology && !record.Topology.gameObject.activeSelf)
            {
                record.Topology.gameObject.SetActive(true);
            }

            record.Topology.RestoreSnapshot(record.After);
            _undo.Push(record);
            return true;
        }

        return false;
    }

    private sealed class EditRecord
    {
        public MXInkEditableMeshTopology Topology;
        public MXInkTopologySnapshot Before;
        public MXInkTopologySnapshot After;
        public bool CreatedTopology;
    }
}
