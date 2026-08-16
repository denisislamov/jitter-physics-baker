using System;
using System.Reflection;
using UnityEngine;

namespace DataSakura.JitterPhysics.Samples
{
    /// <summary>
    /// Reads the sample controls without forcing the package to depend on either Unity input
    /// backend.
    /// </summary>
    internal static class JitterPhysicsSampleInput
    {
#if ENABLE_INPUT_SYSTEM
        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags PublicStatic = BindingFlags.Static | BindingFlags.Public;

        private static readonly Type KeyboardType =
            Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
        private static readonly Type MouseType =
            Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem");

        public static Vector2 Move
        {
            get
            {
                object keyboard = Current(KeyboardType);
                return new Vector2(
                    Axis(keyboard, "aKey", "dKey"),
                    Axis(keyboard, "sKey", "wKey"));
            }
        }

        public static Vector2 LookDelta
        {
            get
            {
                object mouse = Current(MouseType);
                object delta = GetProperty(mouse, "delta");
                object value = delta?.GetType()
                    .GetMethod("ReadValue", PublicInstance, null, Type.EmptyTypes, null)
                    ?.Invoke(delta, null);

                // The new backend reports mouse delta in pixels while the legacy mouse axes are
                // sensitivity-scaled. Keep the sample's serialized sensitivity useful on both.
                return value is Vector2 vector ? vector * 0.05f : Vector2.zero;
            }
        }

        public static bool WasKeyPressedThisFrame(KeyCode key)
        {
            string control = key switch
            {
                KeyCode.Space => "spaceKey",
                KeyCode.Backspace => "backspaceKey",
                KeyCode.Escape => "escapeKey",
                _ => null
            };

            return control != null && Button(Current(KeyboardType), control, "wasPressedThisFrame");
        }

        public static bool IsPrimaryButtonPressed =>
            Button(Current(MouseType), "leftButton", "isPressed");

        public static bool WasSecondaryButtonPressedThisFrame =>
            Button(Current(MouseType), "rightButton", "wasPressedThisFrame");

        private static float Axis(object device, string negative, string positive)
        {
            float value = 0f;
            if (Button(device, negative, "isPressed"))
            {
                value -= 1f;
            }

            if (Button(device, positive, "isPressed"))
            {
                value += 1f;
            }

            return value;
        }

        private static bool Button(object device, string controlName, string stateName)
        {
            object control = GetProperty(device, controlName);
            object state = GetProperty(control, stateName);
            return state is bool value && value;
        }

        private static object Current(Type deviceType)
        {
            return deviceType?.GetProperty("current", PublicStatic)?.GetValue(null);
        }

        private static object GetProperty(object instance, string name)
        {
            return instance?.GetType().GetProperty(name, PublicInstance)?.GetValue(instance);
        }
#else
        public static Vector2 Move => new Vector2(
            Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

        public static Vector2 LookDelta => new Vector2(
            Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

        public static bool WasKeyPressedThisFrame(KeyCode key) => Input.GetKeyDown(key);

        public static bool IsPrimaryButtonPressed => Input.GetMouseButton(0);

        public static bool WasSecondaryButtonPressedThisFrame => Input.GetMouseButtonDown(1);
#endif
    }
}
