// SceneReset.cs - put the room back to its starting state without reloading the scene.
// Attach to "AgentManager". Takes a snapshot right after Play starts (items, cabinets, robot);
// Restore() puts everything back instantly. Used by "Reset room" and by TaskRunner between tasks.
// Works for any room: it snapshots whatever Interactables exist (SPEC 5.6).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HabitatVLM
{
    public class SceneReset : MonoBehaviour
    {
        struct ItemSnap
        {
            public Interactable item;
            public Transform parent;
            public Vector3 position;
            public Quaternion rotation;
            public Interactable location;
        }

        readonly List<ItemSnap> items = new List<ItemSnap>();
        readonly List<(Cabinet cabinet, bool open)> cabinets = new List<(Cabinet, bool)>();
        RobotSkills robot;
        Vector3 robotPos;
        Quaternion robotRot;

        public bool HasSnapshot { get; private set; }

        IEnumerator Start()
        {
            yield return null;   // let every Interactable.Start() find where its item sits first
            Snapshot();
        }

        public void Snapshot()
        {
            items.Clear();
            cabinets.Clear();
            foreach (var it in Interactable.All.Values)
            {
                if (it == null) continue;
                if (it.kind == InteractableKind.Item)
                    items.Add(new ItemSnap
                    {
                        item = it, parent = it.transform.parent,
                        position = it.transform.position, rotation = it.transform.rotation,
                        location = it.location,
                    });
                if (it.Cabinet != null) cabinets.Add((it.Cabinet, it.Cabinet.IsOpen));
            }
            robot = FindAnyObjectByType<RobotSkills>();
            if (robot != null) { robotPos = robot.transform.position; robotRot = robot.transform.rotation; }
            HasSnapshot = true;
        }

        /// Instantly restore the snapshot. Only call while the robot is idle.
        public void Restore()
        {
            if (!HasSnapshot) return;
            if (robot != null) robot.ResetTo(robotPos, robotRot);

            foreach (var it in Interactable.All.Values) if (it != null) it.contents.Clear();
            foreach (var s in items)
            {
                if (s.item == null) continue;
                s.item.transform.SetParent(s.parent, true);
                s.item.transform.SetPositionAndRotation(s.position, s.rotation);
                s.item.isHeld = false;
                s.item.SetCollidersEnabled(true);
                s.item.location = s.location;
                if (s.location != null) s.location.contents.Add(s.item);
            }
            foreach (var (cabinet, open) in cabinets)
                if (cabinet != null) cabinet.SetOpenImmediate(open);
        }
    }
}
