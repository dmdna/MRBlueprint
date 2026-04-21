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

    public bool TryUndo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var record = _undo.Pop();
        if (record.Topology == null)
        {
            return false;
        }

        if (record.CreatedTopology)
        {
            record.Topology.gameObject.SetActive(false);
        }
        else
        {
            record.Topology.RestoreSnapshot(record.Before);
        }

        _redo.Push(record);
        return true;
    }

    public bool TryRedo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        var record = _redo.Pop();
        if (record.Topology == null)
        {
            return false;
        }

        if (record.CreatedTopology && !record.Topology.gameObject.activeSelf)
        {
            record.Topology.gameObject.SetActive(true);
        }

        record.Topology.RestoreSnapshot(record.After);
        _undo.Push(record);
        return true;
    }

    private sealed class EditRecord
    {
        public MXInkEditableMeshTopology Topology;
        public MXInkTopologySnapshot Before;
        public MXInkTopologySnapshot After;
        public bool CreatedTopology;
    }
}
