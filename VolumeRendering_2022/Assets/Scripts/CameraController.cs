using UnityEngine;
using UnityEngine.InputSystem;

namespace VolumeRenderingSample
{
    /// <summary>
    /// 飞行相机，移植自包示例 SampleProject~/Assets/Scripts/CameraController.cs。
    /// 原实现使用旧版 Input API，而本工程 activeInputHandler=1（仅新 Input System），故改写：
    /// WASD/QE 平移、滚轮前后、按住右键拖拽旋转，Shift 加速。
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        // 移动速度（米/秒，体数据按真实尺寸以米为单位）
        public float movementSpeed = 1.2f;
        // 旋转灵敏度（度/像素）
        public float rotationSpeed = 0.1f;
        // 滚轮缩放（米/格）
        public float scrollSpeed = 0.5f;
        // Shift 按住时的移动速度倍率
        public float shiftSpeedMultiplier = 3.0f;
        // 位置/旋转插值速度
        public float smoothingSpeed = 15.0f;

        private Vector3 positionDelta = Vector3.zero;
        private Vector2 rotationDelta = Vector2.zero;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null || mouse == null)
                return;

            float actualMovementSpeed = movementSpeed * (keyboard.shiftKey.isPressed ? shiftSpeedMultiplier : 1.0f);

            Vector3 movementDir = Vector3.zero;
            if (keyboard.wKey.isPressed) movementDir.z += actualMovementSpeed;
            if (keyboard.sKey.isPressed) movementDir.z -= actualMovementSpeed;
            if (keyboard.dKey.isPressed) movementDir.x += actualMovementSpeed;
            if (keyboard.aKey.isPressed) movementDir.x -= actualMovementSpeed;
            if (keyboard.eKey.isPressed) movementDir.y += actualMovementSpeed;
            if (keyboard.qKey.isPressed) movementDir.y -= actualMovementSpeed;
            movementDir.z += mouse.scroll.ReadValue().y * scrollSpeed;

            Vector3 worldMovementDir = transform.TransformDirection(movementDir);
            Vector3 targetPositionDelta = worldMovementDir * Time.deltaTime;
            positionDelta = Vector3.Lerp(positionDelta, targetPositionDelta, Time.deltaTime * smoothingSpeed);
            transform.position += positionDelta;

            if (mouse.rightButton.isPressed)
            {
                Vector2 mouseDelta = mouse.delta.ReadValue();
                Vector2 targetRotationDelta = new Vector2(mouseDelta.x * rotationSpeed, -mouseDelta.y * rotationSpeed);
                rotationDelta = Vector2.Lerp(rotationDelta, targetRotationDelta, Time.deltaTime * smoothingSpeed);
                transform.Rotate(new Vector3(rotationDelta.y, 0.0f, 0.0f), Space.Self);
                transform.Rotate(new Vector3(0.0f, rotationDelta.x, 0.0f), Space.World);
            }
        }
    }
}
