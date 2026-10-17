using UnityEngine;

namespace MagicPigGames
{
    public abstract class LazySingleton<T> : MonoBehaviour where T : Component
    {
        private static T _instance;

        public static T Instance
        { 
            get 
            {
                if (_instance != null) 
                    return _instance;
                
                // FindObjectOfType is deprecated in Unity 6. FindAnyObjectByType exists back to 6.0 and
                // is the right call for a singleton lookup - it stops at the first match instead of
                // sorting the full result set.
                _instance = FindAnyObjectByType<T>();

                if (_instance != null) 
                    return _instance;
                
                var singletonObject = new GameObject();
                _instance = singletonObject.AddComponent<T>();
                DontDestroyOnLoad(singletonObject);
                return _instance;
            }
        }
        
        // Ensures no other components can create an instance
        protected LazySingleton() {} 
    }
}