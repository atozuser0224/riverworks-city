using UnityEngine;

namespace Riverworks
{
    /// <summary>Legacy touch input shared by the city and factory views.</summary>
    public sealed class MobileInput : MonoBehaviour
    {
        const float MinPinchDistance = 8f;
        const float DragThresholdPixels = 12f;

        GameController game;
        int primaryFinger = -1;
        Vector2 startPosition;
        Vector2 lastPosition;
        bool gestureBlocked;
        bool dragged;
        bool hadMultipleTouches;
        bool initialized;

        public void Initialize(GameController controller)
        {
            game = controller;
            initialized = controller != null;
            if (Application.isMobilePlatform) Input.simulateMouseWithTouches = false;
            enabled = initialized && Application.isMobilePlatform;
        }

        void Update()
        {
            if (!initialized || game == null) return;
            if (HandleBack()) return;

            if (game.SmokeMode || game.HelpOpen || game.ResearchOpen || game.ModalOpen || (game.Tutorial!=null&&game.Tutorial.IntroPlaying))
            {
                ResetGesture();
                return;
            }

            int count = Input.touchCount;
            if (count <= 0)
            {
                ResetGesture();
                return;
            }

            if (count >= 2)
            {
                HandleTwoFinger(Input.GetTouch(0), Input.GetTouch(1));
                return;
            }

            HandleOneFinger(Input.GetTouch(0));
        }

        bool HandleBack()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            ResetGesture();
            if(game.Tutorial!=null&&game.Tutorial.IntroPlaying){game.Tutorial.SkipIntro();return true;}
            // Close the frontmost window before changing the underlying construction tool.
            if (game.ModalOpen) { game.ModalOpen = false; return true; }
            if (game.ResearchOpen) { game.ToggleResearch(); return true; }
            if (game.HelpOpen) { game.ToggleHelp(); return true; }
            var factory = game.Factory;
            if (factory != null && factory.IsOpen)
            {
                if (factory.SelectedTool != FactoryKind.None || factory.RemovalMode) factory.SelectTool(FactoryKind.None);
                else factory.Close();
                return true;
            }
            game.SelectTool(BuildingKind.None);
            return true;
        }

        void HandleOneFinger(Touch touch)
        {
            if (touch.phase == TouchPhase.Began || primaryFinger < 0)
            {
                primaryFinger = touch.fingerId;
                startPosition = lastPosition = touch.position;
                dragged = false;
                gestureBlocked = hadMultipleTouches || IsOverUI(touch.position);
                return;
            }
            if (touch.fingerId != primaryFinger)
            {
                gestureBlocked = true;
                return;
            }

            if (IsOverUI(touch.position)) gestureBlocked = true;
            Vector2 delta = touch.position - lastPosition;
            if (!dragged && (touch.position-startPosition).sqrMagnitude >= DragThresholdPixels*DragThresholdPixels) dragged = true;

            if (!gestureBlocked && dragged && !HasPlacementTool()) Pan(delta);

            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                if (!gestureBlocked && !dragged && !hadMultipleTouches && touch.phase == TouchPhase.Ended)
                    Interact(touch.position);
                ResetGesture();
                return;
            }
            lastPosition = touch.position;
        }

        void HandleTwoFinger(Touch first, Touch second)
        {
            hadMultipleTouches = true;
            dragged = true;
            if (first.phase == TouchPhase.Began || second.phase == TouchPhase.Began)
                gestureBlocked = gestureBlocked || IsOverUI(first.position) || IsOverUI(second.position);
            if (IsOverUI(first.position) || IsOverUI(second.position)) gestureBlocked = true;
            if (gestureBlocked) return;

            Vector2 oldFirst = first.position-first.deltaPosition;
            Vector2 oldSecond = second.position-second.deltaPosition;
            Vector2 oldMidpoint = (oldFirst+oldSecond)*.5f;
            Vector2 midpoint = (first.position+second.position)*.5f;
            Pan(midpoint-oldMidpoint);

            float oldDistance = Vector2.Distance(oldFirst,oldSecond);
            float distance = Vector2.Distance(first.position,second.position);
            if (oldDistance >= MinPinchDistance && distance >= MinPinchDistance)
                Zoom(oldDistance/distance);
        }

        bool HasPlacementTool()
        {
            var factory = game.Factory;
            if (factory != null && factory.IsOpen)
                return factory.SelectedTool != FactoryKind.None || factory.RemovalMode;
            return game.SelectedTool != BuildingKind.None || game.DemolitionMode;
        }

        bool IsOverUI(Vector2 position)
        {
            var factory = game.Factory;
            return factory != null && factory.IsOpen
                ? factory.IsScreenPointOverUI(position)
                : game.IsScreenPointOverUI(position);
        }

        void Interact(Vector2 position)
        {
            var factory = game.Factory;
            game.InteractScreenPoint(position);
        }

        void Pan(Vector2 delta)
        {
            var factory = game.Factory;
            if (factory != null && factory.IsOpen) factory.View.PanScreen(delta);
            else game.CameraRig.PanScreen(delta);
        }

        void Zoom(float multiplier)
        {
            var factory = game.Factory;
            if (factory != null && factory.IsOpen) factory.View.Zoom(multiplier);
            else game.CameraRig.Zoom(multiplier);
        }

        void ResetGesture()
        {
            primaryFinger = -1;
            gestureBlocked = false;
            dragged = false;
            hadMultipleTouches = false;
        }
    }
}
