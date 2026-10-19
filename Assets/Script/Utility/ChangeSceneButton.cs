using UnityEngine;
using UnityEngine.SceneManagement;

public class ChangeSceneButton : MonoBehaviour
{

    private string _currentScene;
    
    private void Start()
    {
        _currentScene = SceneManager.GetActiveScene().name;
    }

    public void ChangeScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }

    public void ResetScene()
    {
        SceneManager.LoadScene(_currentScene);
    }
}
