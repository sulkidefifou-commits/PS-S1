using UnityEngine;

public class DebugRegisterObject : MonoBehaviour
{

    public bool object1;
    public bool object2;
    
    void Start()
    {
        gameObject.SetActive(false);
        
        if (object1 && !object2)
        {
            SuikaManager.Instance.RegisterDebugObject1(gameObject);
        }
        else if (object2 && !object1)
        {
            SuikaManager.Instance.RegisterDebugObject2(gameObject);
        }
        
    }

}
