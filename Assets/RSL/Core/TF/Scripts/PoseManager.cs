using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.Events;

namespace RSL.Core.TF
{
    #if UNITY_EDITOR
    using UnityEditor;

    [CustomEditor(typeof(PoseManager))]
    public class PoseManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PoseManager myScript = (PoseManager)target;
            if (GUILayout.Button("Center robot"))
            {
                myScript.BaseToLocation(Vector3.zero);
            }
            if (GUILayout.Button("Fix location"))
            {
                myScript.SetFixedLocation(true);
            }
            if (GUILayout.Button("Unfix location"))
            {
                myScript.SetFixedLocation(false);
            }
            if (GUILayout.Button("Lock"))
            {
                myScript.SetLocked(true);
            }
            if (GUILayout.Button("Unlock"))
            {
                myScript.SetLocked(false);
            }
        }
    }
    #endif

    public class PoseManager : MonoBehaviour
    {
        public static PoseManager Instance { get; private set; }

        public InputActionReference joystickXY;
        public InputActionReference joystickZR;
        public InputActionReference alt;
        public InputActionReference action;
        public InputActionReference bAction;

        public UnityEvent actions;
        public UnityEvent bActions;

        public GameObject sphere;
        public GameObject floor;
        public float speed = 1.0f;
        public float rotationTolerance = 5.0f;
        public bool handSelectable = false;
        public bool findRoot = true;
        public Transform root;
        public Transform _root;
        private Transform _mainCamera;
        public Transform _robot;

        public bool _locked = false;
        private Vector3 _center;
        private Vector3 _forward;
        private bool _fixedLocation = false;
        // Whether _center/_forward hold the robot pose to keep while _fixedLocation is on.
        private bool _anchored = false;
        private bool _notifiedMissingRobot = false;

        void Awake()
        {
            // if (Instance != null && Instance != this)
            //     Destroy(this);
            // else
            //     Instance = this;
            Instance = this;
        }

        public static PoseManager GetOrCreateInstance()
        {
            // if (Instance == null)
            // {
            //     GameObject go = new GameObject();
            //     go.name = "PoseManager";
            //     return go.AddComponent<PoseManager>();
            // }
            return Instance;
        }

        void Start()
        {
            if (root == null)
            {
                root = transform;
            }
            // need to ensure this happens after tf init....
            _mainCamera = Camera.main.transform;

            if (actions != null && action == null)
            {
                actions = null;
                Debug.LogWarning("PoseManager: actions set but action not set");
            }

            GameObject robot = GameObject.FindWithTag("robot");

            if (robot == null)
                Debug.LogWarning("PoseManager: robot not found");        
            else
                _robot = robot.transform;

            sphere.SetActive(false);

        }

        void Update()
        {
            if(_robot == null)
            {
                GameObject robot = GameObject.FindWithTag("robot");
                if (robot == null)
                {
                    if (!_notifiedMissingRobot)
                    {
                        Debug.LogWarning("PoseManager: robot not found");
                        _notifiedMissingRobot = true;
                    }
                }
                else
                {
                    _robot = robot.transform;
                }
            }
        // }

        // void OnPreRender()
        // {
            

            while (root.parent != null && findRoot)
            {
                // Climb all the way to the top of the TF tree, through the
                // "root" anchor (ModelManager._root). The anchor is a CHILD of
                // the root-frame object, a sibling of the robot's own frames
                // ("base" and below), so moving it moved the axis and sensor
                // visuals but left the robot behind. The tree's top is safe
                // to move by hand: TF only writes a frame's pose when it is
                // some transform's child (TF2Stream.Add, StaticRemapper), and
                // this loop runs every frame, so if a new parent appears
                // above it (e.g. map -> odom) we just keep climbing.
                root = root.parent;
                _root = root;
                Debug.Log("root frame: " + root);

            }
            if (_root == null)
            {
                _root = root;
            }

            if (action?.action.IsPressed() ?? false)
            {
                if (alt?.action.IsPressed() ?? false)
                {
                    ResetScale();
                }
                else
                {
                    actions?.Invoke();
                }
            }

            if(bAction?.action.IsPressed() ?? false)
            {
                bActions?.Invoke();
            }

            if(_locked)
                return;
            if (joystickXY.action.IsPressed() || joystickZR.action.IsPressed())
            {
                if (alt?.action.IsPressed() ?? false)
                    Scale(joystickXY.action.ReadValue<Vector2>());
                else
                    Move(joystickXY.action.ReadValue<Vector2>());

                OffsetRotate(joystickZR.action.ReadValue<Vector2>());
                // Keep the robot where the user just put it.
                _anchored = false;

                sphere.SetActive(true);
            }
            else
            {
                sphere.SetActive(false);
            }
            if(floor != null)
            {
                floor.transform.position = new Vector3(_root.position.x, 0, _root.position.z);
            }
        }

        public void SetLocked(bool locked)
        {
            _locked = locked;
        }

        void ResetScale()
        {
            _root.localScale = Vector3.one;
        }


        // LateUpdate so the hold runs after this frame's TF updates have moved the robot.
        void LateUpdate()
        {
            if (!_fixedLocation || _robot == null || _root == null)
                return;
            if (!_anchored)
            {
                _center = _robot.position;
                _forward = Vector3.ProjectOnPlane(_robot.forward, Vector3.up).normalized;
                _anchored = _forward != Vector3.zero;
                return;
            }
            HoldRobot();
        }

        // Moves the scene, not the camera, so the robot keeps its anchored heading and horizontal
        // position while it walks. Height is left alone so the world does not bob with the base.
        void HoldRobot()
        {
            Vector3 forward = Vector3.ProjectOnPlane(_robot.forward, Vector3.up);
            if (forward != Vector3.zero)
                _root.RotateAround(_robot.position, Vector3.up, Vector3.SignedAngle(forward, _forward, Vector3.up));
            Vector3 offset = _center - _robot.position;
            offset.y = 0;
            _root.position += offset;
        }

        public void SetFixedLocation(bool fixedLocation)
        {
            _fixedLocation = fixedLocation;
            _anchored = false;
            if (!fixedLocation)
            {
                _center = Vector3.zero;
                _forward = Vector3.zero;
            }
        }

        void Move(Vector2 input)
        {
            Vector3 move = new Vector3(input.x, 0, input.y);
            // move = root.TransformDirection(move);

            // get relative to the player's view point
            move = _mainCamera.TransformDirection(move);
            // take into account the gameobject's orientation
            // zero out the vertical component
            move.y = 0;
            // move the gameobject relative to the player regardless of gameobject orientation
            _root.Translate(move * speed * Time.deltaTime, Space.World);
        }

        void Scale(Vector2 input)
        {
            Vector3 scale = new Vector3(input.x, input.x, input.x);
            scale = Vector3.Scale(_root.localScale, scale);
            _root.localScale += scale * speed * Time.deltaTime;
        }

        void OffsetRotate(Vector2 input)
        {
            // offset on the y axis based on forwards/back on second joystick
            Vector3 move = new Vector3(0, input.y, 0);

            // rotate on the x axis based on left/right on second joystick
            _root.Translate(move * speed * Time.deltaTime / 10);
            _root.Rotate(0, input.x * speed * Time.deltaTime * 20, 0);
        }

        public void ClickCb(SelectEnterEventArgs args)
        {
            if (_root == null || !handSelectable) return;

            Vector3 position;
            UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor = (UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor)args.interactorObject;
            rayInteractor.TryGetHitInfo(out position, out _, out _, out _);
            _root.position = position;
            _anchored = false;
        }

        public void BaseToLocation(Vector3 position)
        {
            Vector3 offset = position - _robot.position;
            _root.position += offset;

            float angle = Vector3.SignedAngle(_robot.forward, _forward, Vector3.up);
            if (_forward != Vector3.zero && Mathf.Abs(angle) > rotationTolerance)
            {
                _root.Rotate(0, angle, 0);
            }
            _anchored = false;
        }

        public void ToggleLocked()
        {
            _locked = !_locked;
        }
    }
}
