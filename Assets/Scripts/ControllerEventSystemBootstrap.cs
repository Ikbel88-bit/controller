using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace RoboClashController
{
    public static class ControllerEventSystemBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnLoad()
        {
            Ensure();
        }

        public static EventSystem Ensure()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                eventSystem = Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            eventSystem.gameObject.SetActive(true);
            eventSystem.enabled = true;

#if ENABLE_INPUT_SYSTEM
            var legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
                Object.Destroy(legacyModule);

            var inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                inputModule.AssignDefaultActions();
            }
            inputModule.enabled = true;
#else
            if (eventSystem.GetComponent<StandaloneInputModule>() == null)
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
#endif
            return eventSystem;
        }
    }
}
