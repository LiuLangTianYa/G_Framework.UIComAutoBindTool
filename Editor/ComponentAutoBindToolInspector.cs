using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using BindData = ComponentAutoBindTool.BindData;
using System.Reflection;
using System.IO;
using UnityEditor.Callbacks;
using System.Text;
using GameFrameX;
using GameFrameX.Runtime;
using UnityEngine.UI;

[CustomEditor(typeof(ComponentAutoBindTool))]
public class ComponentAutoBindToolInspector : Editor
{

    private ComponentAutoBindTool m_Target;
    private static ComponentAutoBindTool m_target;

    private SerializedProperty m_BindDatas;
    private SerializedProperty m_BindComs;
    private List<BindData> m_TempList = new List<BindData>();
    private List<string> m_TempFiledNames = new List<string>();
    private List<string> m_TempComponentTypeNames = new List<string>();

    private string[] s_AssemblyNames = { "Unity.Startup" };
    private string[] m_HelperTypeNames;
    private string m_HelperTypeName;
    private int m_HelperTypeNameIndex;

    private AutoBindGlobalSetting m_Setting;

    private SerializedProperty m_Namespace;
    private SerializedProperty m_ClassName;
    private SerializedProperty m_ComCodePath;
    private SerializedProperty m_MountCodePath;

    private void OnEnable()
    {
        m_Target = (ComponentAutoBindTool)target;
        m_target = (ComponentAutoBindTool)target;
        m_BindDatas = serializedObject.FindProperty("BindDatas");
        m_BindComs = serializedObject.FindProperty("m_BindComs");

        m_HelperTypeNames = GetTypeNames(typeof(IAutoBindRuleHelper), s_AssemblyNames);
        m_Setting = AutoBindGlobalSetting.GetAutoBindGlobalSetting();
        m_Namespace = serializedObject.FindProperty("m_Namespace");
        m_ClassName = serializedObject.FindProperty("m_ClassName");
        m_ComCodePath = serializedObject.FindProperty("m_ComCodePath");
        m_MountCodePath = serializedObject.FindProperty("m_MountCodePath");

        m_Namespace.stringValue = string.IsNullOrEmpty(m_Namespace.stringValue) ? m_Setting.Namespace : m_Namespace.stringValue;
        m_ClassName.stringValue = string.IsNullOrEmpty(m_ClassName.stringValue) ? m_Target.gameObject.name : m_ClassName.stringValue;
        m_ComCodePath.stringValue = string.IsNullOrEmpty(m_ComCodePath.stringValue) ? m_Setting.ComCodePath : m_ComCodePath.stringValue;
        m_MountCodePath.stringValue = string.IsNullOrEmpty(m_MountCodePath.stringValue) ? m_Setting.MountCodePath : m_MountCodePath.stringValue;

        serializedObject.ApplyModifiedProperties();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawTopButton();

        DrawHelperSelect();

        DrawSetting();

        DrawKvData();

        serializedObject.ApplyModifiedProperties();


    }

    /// <summary>
    /// 绘制顶部按钮
    /// </summary>
    private void DrawTopButton()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("排序"))
        {
            Sort();
        }

        if (GUILayout.Button("全部删除"))
        {
            RemoveAll();
        }

        if (GUILayout.Button("删除空引用"))
        {
            RemoveNull();
        }

        if (GUILayout.Button("自动绑定组件"))
        {
            AutoBindComponent();
        }

        if (GUILayout.Button("生成绑定代码"))
        {
            if (m_ClassName.stringValue.Equals("UIForm") || string.IsNullOrEmpty(m_ClassName.stringValue))
            {
                EditorUtility.DisplayDialog("提示", "请先同步物体名字到类名，点击【物体名】按钮", "确定");
                return;
            }
            //GenAutoBindCode();
            GenAutoBindMountCode();
        }
        if (GUILayout.Button("挂载逻辑代码"))
        {
            string className = !string.IsNullOrEmpty(m_Target.ClassName) ? m_Target.ClassName : m_Target.gameObject.name;
            if (!m_Target.gameObject.GetComponent(className))
            {
                Type _type = GetTypeWithName(className,m_Target.Namespace);
                if (_type != null)
                {
                    m_Target.gameObject.AddComponent(_type);
                }
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 排序
    /// </summary>
    private void Sort()
    {


        m_TempList.Clear();
        foreach (BindData data in m_Target.BindDatas)
        {
            m_TempList.Add(new BindData(data.Name, data.BindCom));
        }
        m_TempList.Sort((x, y) =>
        {
            return string.Compare(x.Name, y.Name, StringComparison.Ordinal);
        });

        m_BindDatas.ClearArray();
        foreach (BindData data in m_TempList)
        {
            AddBindData(data.Name, data.BindCom);
        }

        SyncBindComs();
    }

    /// <summary>
    /// 全部删除
    /// </summary>
    private void RemoveAll()
    {
        m_BindDatas.ClearArray();

        SyncBindComs();
    }

    /// <summary>
    /// 删除空引用
    /// </summary>
    private void RemoveNull()
    {
        for (int i = m_BindDatas.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty element = m_BindDatas.GetArrayElementAtIndex(i).FindPropertyRelative("BindCom");
            if (element.objectReferenceValue == null)
            {
                m_BindDatas.DeleteArrayElementAtIndex(i);
            }
        }

        SyncBindComs();
    }

    /// <summary>
    /// 自动绑定组件
    /// </summary>
    private void AutoBindComponent()
    {
        m_BindDatas.ClearArray();

        Transform[] childs = m_Target.gameObject.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in childs)
        {
            m_TempFiledNames.Clear();
            m_TempComponentTypeNames.Clear();
            if (child == m_Target.transform)
            {
                continue;
            }
            ComponentAutoBindTool componentAuto1 = child.gameObject.GetComponent<ComponentAutoBindTool>();
            ComponentAutoBindTool componentAuto = child.gameObject.GetComponentInParent<ComponentAutoBindTool>(true);
            if (componentAuto1 == null)
            {
                if (componentAuto != null && componentAuto != m_Target)
                {
                    continue;
                }
            }
            if (AutoBindGlobalSetting.IsValidBind(child, m_TempFiledNames, m_TempComponentTypeNames))
            {
                for (int i = 0; i < m_TempFiledNames.Count; i++)
                {
                    Component com = child.GetComponent(m_TempComponentTypeNames[i]);
                    if (com == null)
                    {
                        //Log.Warning($"{child.name}上不存在{m_TempComponentTypeNames[i]}的组件");
                        continue;
                    }
                    else
                    {
                        string newFiledName = m_TempFiledNames[i].Replace("#", "");
                        AddBindData(newFiledName, child.GetComponent(m_TempComponentTypeNames[i]));
                        break;
                    }

                }
            }
        }

        SyncBindComs();
    }

    /// <summary>
    /// 绘制辅助器选择框
    /// </summary>
    private void DrawHelperSelect()
    {
        m_HelperTypeName = m_HelperTypeNames[0];

        if (m_Target.RuleHelper != null)
        {
            m_HelperTypeName = m_Target.RuleHelper.GetType().Name;

            for (int i = 0; i < m_HelperTypeNames.Length; i++)
            {
                if (m_HelperTypeName == m_HelperTypeNames[i])
                {
                    m_HelperTypeNameIndex = i;
                }
            }
        }
        else
        {
            IAutoBindRuleHelper helper = (IAutoBindRuleHelper)CreateHelperInstance(m_HelperTypeName, s_AssemblyNames);
            m_Target.RuleHelper = helper;
        }

        foreach (GameObject go in Selection.gameObjects)
        {
            ComponentAutoBindTool autoBindTool = go.GetComponent<ComponentAutoBindTool>();
            if (autoBindTool != null && autoBindTool.RuleHelper == null)
            {
                IAutoBindRuleHelper helper = (IAutoBindRuleHelper)CreateHelperInstance(m_HelperTypeName, s_AssemblyNames);
                autoBindTool.RuleHelper = helper;
            }
        }

        int selectedIndex = EditorGUILayout.Popup("AutoBindRuleHelper", m_HelperTypeNameIndex, m_HelperTypeNames);
        if (selectedIndex != m_HelperTypeNameIndex)
        {
            m_HelperTypeNameIndex = selectedIndex;
            m_HelperTypeName = m_HelperTypeNames[selectedIndex];
            IAutoBindRuleHelper helper = (IAutoBindRuleHelper)CreateHelperInstance(m_HelperTypeName, s_AssemblyNames);
            m_Target.RuleHelper = helper;

        }
    }

    /// <summary>
    /// 绘制设置项
    /// </summary>
    private void DrawSetting()
    {
        EditorGUILayout.BeginHorizontal();
        m_Namespace.stringValue = EditorGUILayout.TextField(new GUIContent("命名空间："), m_Namespace.stringValue);
        if (GUILayout.Button("默认设置"))
        {
            m_Namespace.stringValue = m_Setting.Namespace;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        m_ClassName.stringValue = EditorGUILayout.TextField(new GUIContent("类名："), m_ClassName.stringValue);
        if (GUILayout.Button("物体名"))
        {
            m_ClassName.stringValue = m_Target.gameObject.name;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("组件代码保存路径：");
        EditorGUILayout.LabelField(m_ComCodePath.stringValue);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("选择组件代码路径"))
        {
            string folder = Path.Combine(Application.dataPath, serializedObject.FindProperty("m_ClassName").stringValue+ m_ComCodePath.stringValue);
            if (!Directory.Exists(folder))
            {
                folder = Application.dataPath;
            }
            string path = EditorUtility.OpenFolderPanel("选择组件代码保存路径", folder, "");
            if (!string.IsNullOrEmpty(path))
            {
                m_ComCodePath.stringValue = path.Replace(Application.dataPath +"/", "");
            }
        }
        if (GUILayout.Button("默认设置"))
        {
            m_ComCodePath.stringValue = m_Setting.ComCodePath;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("挂载代码保存路径：");
        EditorGUILayout.LabelField(m_MountCodePath.stringValue);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("选择挂载代码路径"))
        {
            string folder = Path.Combine(Application.dataPath, m_MountCodePath.stringValue);
            if (!Directory.Exists(folder))
            {
                folder = Application.dataPath;
            }
            string path = EditorUtility.OpenFolderPanel("选择挂载代码保存路径", folder, "");
            if (!string.IsNullOrEmpty(path))
            {
                m_MountCodePath.stringValue = path.Replace(Application.dataPath +"/", "");
            }
        }
        if (GUILayout.Button("默认设置"))
        {
            m_MountCodePath.stringValue = m_Setting.ComCodePath;
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 绘制键值对数据
    /// </summary>
    private void DrawKvData()
    {
        //绘制key value数据

        int needDeleteIndex = -1;

        EditorGUILayout.BeginVertical();
        SerializedProperty property;

        for (int i = 0; i < m_BindDatas.arraySize; i++)
        {

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"[{i}]", GUILayout.Width(25));
            property = m_BindDatas.GetArrayElementAtIndex(i).FindPropertyRelative("Name");
            property.stringValue = EditorGUILayout.TextField(property.stringValue, GUILayout.Width(150));
            property = m_BindDatas.GetArrayElementAtIndex(i).FindPropertyRelative("BindCom");
            property.objectReferenceValue = EditorGUILayout.ObjectField(property.objectReferenceValue, typeof(Component), true);

            if (GUILayout.Button("X"))
            {
                //将元素下标添加进删除list
                needDeleteIndex = i;
            }
            EditorGUILayout.EndHorizontal();
        }

        //删除data
        if (needDeleteIndex != -1)
        {
            m_BindDatas.DeleteArrayElementAtIndex(needDeleteIndex);
            SyncBindComs();
        }

        EditorGUILayout.EndVertical();
    }



    /// <summary>
    /// 添加绑定数据
    /// </summary>
    private void AddBindData(string name, Component bindCom)
    {
        for (int i = 0; i < m_BindDatas.arraySize; i++)
        {
            SerializedProperty elementData = m_BindDatas.GetArrayElementAtIndex(i);
            if (elementData.FindPropertyRelative("Name").stringValue == name)
            {
                Debug.LogError($"有重复名字！请检查后重新生成！Name:{name}");
                return;
            }
        }
        int index = m_BindDatas.arraySize;
        m_BindDatas.InsertArrayElementAtIndex(index);
        SerializedProperty element = m_BindDatas.GetArrayElementAtIndex(index);
        element.FindPropertyRelative("Name").stringValue = name;
        element.FindPropertyRelative("BindCom").objectReferenceValue = bindCom;

    }

    /// <summary>
    /// 同步绑定数据
    /// </summary>
    private void SyncBindComs()
    {
        m_BindComs.ClearArray();

        for (int i = 0; i < m_BindDatas.arraySize; i++)
        {
            SerializedProperty property = m_BindDatas.GetArrayElementAtIndex(i).FindPropertyRelative("BindCom");
            m_BindComs.InsertArrayElementAtIndex(i);
            m_BindComs.GetArrayElementAtIndex(i).objectReferenceValue = property.objectReferenceValue;
        }
    }

    /// <summary>
    /// 获取指定基类在指定程序集中的所有子类名称
    /// </summary>
    private string[] GetTypeNames(Type typeBase, string[] assemblyNames)
    {
        List<string> typeNames = new List<string>();
        foreach (string assemblyName in assemblyNames)
        {
            Assembly assembly = null;
            try
            {
                assembly = Assembly.Load(assemblyName);
            }
            catch
            {
                continue;
            }

            if (assembly == null)
            {
                continue;
            }

            Type[] types = assembly.GetTypes();
            foreach (Type type in types)
            {
                if (type.IsClass && !type.IsAbstract && typeBase.IsAssignableFrom(type))
                {
                    typeNames.Add(type.FullName);
                }
            }
        }

        typeNames.Sort();
        return typeNames.ToArray();
    }

    /// <summary>
    /// 创建辅助器实例
    /// </summary>
    private object CreateHelperInstance(string helperTypeName, string[] assemblyNames)
    {
        foreach (string assemblyName in assemblyNames)
        {
            Assembly assembly = Assembly.Load(assemblyName);

            object instance = assembly.CreateInstance(helperTypeName);
            if (instance != null)
            {
                return instance;
            }
        }

        return null;
    }

    /// <summary>
    /// 写入第三方引用
    /// </summary>
    /// <param name="streamWriter">写入流</param>
    private void WriteUsing(StreamWriter streamWriter)
    {
        usingSameStr.Clear();
        //根据索引获取
        for (int i = 0; i < m_Target.BindDatas.Count; i++)
        {
            BindData data = m_Target.BindDatas[i];
            if (!string.IsNullOrEmpty(data.BindCom.GetType().Namespace))
            {
                if (usingSameStr.Contains(data.BindCom.GetType().Namespace))
                {
                    continue;
                }
                usingSameStr.Add(data.BindCom.GetType().Namespace);
                streamWriter.WriteLine($"using {data.BindCom.GetType().Namespace};");
            }
        }
    }
    private List<string> usingSameStr = new List<string>();
    private void ModifyFileFormat(string filePath) 
    {
        string text = "";
        using (StreamReader read = new StreamReader(filePath))
        {
            string oldtext = read.ReadToEnd();
            text = oldtext;
            text = text.Replace("\n", "\r\n");
            text = text.Replace("\r\r\n", "\r\n"); // 防止替换了正常的换行符      
            if (oldtext.Length == text.Length)
            {
                // 如果没有变化就退出
            }
        }
        File.WriteAllText(filePath, text, Encoding.UTF8); //utf-8格式保存，防止乱码
        AssetDatabase.Refresh();
        //EditorUtility.DisplayDialog("提示", "代码生成完毕,正在挂载", "OK");
    }

    /// <summary>
    /// 生成自动绑定的挂载代码
    /// </summary>
    private void GenAutoBindMountCode()
    {
        GameObject go = m_Target.gameObject;
        string className = !string.IsNullOrEmpty(m_Target.ClassName) ? m_Target.ClassName : go.name;
        string codePath = !string.IsNullOrEmpty(m_Target.MountCodePath) ? m_Target.MountCodePath : m_Setting.MountCodePath;
        codePath = Path.Combine(Application.dataPath, codePath);
        if (!Directory.Exists(codePath))
        {
            //Debug.LogError($"挂载{go.name}的代码保存路径{codePath}无效");
            Directory.CreateDirectory(codePath);
            //return;
        }
        string folderName = GetClassFolderName(className);
        codePath = $"{codePath}/{folderName}";
        if (!Directory.Exists(codePath))
        {
            Directory.CreateDirectory(codePath);
        }
        
        string btnMember =   "/*--------------------Auto generate Member variables button .Do not modify!--------------------*/";
        string btnStart =   "/*--------------------Auto generate start button listener.Do not modify!--------------------*/";
        string btnEnd =     "/*--------------------Auto generate end button listener.Do not modify!----------------------*/";
        string scriptEnd =  "/*--------------------Auto generate footer.Do not add anything below the footer!------------*/";
        string scriptEndK = "\t}\n}";
        Dictionary<string, string> clickFuncDict = new Dictionary<string, string>();
        for (int i = 0; i < m_Target.BindDatas.Count; i++)
        {
            if (m_Target.BindDatas[i].BindCom.GetType() == typeof(Button))
                clickFuncDict[$"m_{m_Target.BindDatas[i].Name}"] = $"{m_Target.BindDatas[i].Name}Event"+"Button";
            else if (m_Target.BindDatas[i].BindCom.GetType() == typeof(Toggle))
                clickFuncDict[$"m_{m_Target.BindDatas[i].Name}"] = $"{m_Target.BindDatas[i].Name}Event"+"Toggle";
        }
        string filePath = $"{codePath}/{className}.cs";
        //string filePath = $"{codePath}/{className}.txt";
        if (!File.Exists(filePath))
        {
            using (StreamWriter sw = new StreamWriter(filePath))
            {
    
                sw.WriteLine("using GameFrameX.Runtime;");
                sw.WriteLine("using GameFrameX.UI.Runtime;");
                sw.WriteLine("using GameFrameX.UI.UGUI.Runtime;");
                sw.WriteLine("using UnityEngine;");
                sw.WriteLine("using UnityEngine.UI;");
    
                if (!string.IsNullOrEmpty(m_Target.Namespace))
                    sw.WriteLine($"\nnamespace {m_Target.Namespace}" + "\n{");
                else
                    sw.WriteLine("\nnamespace PleaseAmendNamespace\n{");
    
                sw.WriteLine($"\t/// <summary>\n\t/// Please modify the description.\n\t/// </summary>");
                sw.WriteLine("\tpublic class " + className + " : UGUI\n\t{");
                sw.WriteLine(btnMember);

                //组件字段
                foreach (BindData data in m_Target.BindDatas)
                {
                    sw.WriteLine($"\t\tprivate {data.BindCom.GetType().Name} m_{data.Name};");
                }

                sw.WriteLine();
                //获取autoBindTool上的Component

                sw.WriteLine("\t\tpublic override void OnOpen(object userData){");
                sw.WriteLine("\t\t\t base.OnOpen(userData);");         //   OnInit
                sw.WriteLine(btnStart);
                sw.WriteLine($"\t\t\tComponentAutoBindTool autoBindTool = gameObject.GetComponent<ComponentAutoBindTool>();");

                //根据索引获取
                for (int i = 0; i < m_Target.BindDatas.Count; i++)
                {
                    BindData data = m_Target.BindDatas[i]; 
                    string filedName = $"m_{data.Name}";
                    sw.WriteLine($"\t\t\t{filedName} = autoBindTool.GetBindComponent<{data.BindCom.GetType().Name}>({i});");
                }
                #region OnInit
                //sw.WriteLine("\t\t\t   GetBindComponents(gameObject);\n");         //   OnInit

                //sw.WriteLine("\t\tpublic override void OnOpen(object userData) {\n\t\t\t base.OnOpen(userData);\n\t\t\t    GetBindComponents(gameObject);\n");         //   OnInit
                foreach (var clickFunc in clickFuncDict)
                {
                    if (clickFunc.Value.Contains("Button"))
                     sw.WriteLine($"\t\t\t{clickFunc.Key}.onClick.AddListener(Click{clickFunc.Value.Replace("Button","")});");
                     else if (clickFunc.Value.Contains("Toggle"))
                    {
                        sw.WriteLine($"\t\t\t{clickFunc.Key}.onValueChanged.AddListener((bool ison) =>{{Click{clickFunc.Value.Replace("Toggle","")}(ison);}});");//
                    }
                }
                sw.WriteLine(btnEnd);
                sw.WriteLine("\t\t}\n");
                #endregion
    
                #region ButtonEvent
                for (int i = 0; i < m_Target.BindDatas.Count; i++)
                {
                    if (m_Target.BindDatas[i].BindCom.GetType() == typeof(Button))
                    {
                        sw.WriteLine("\t\tprivate void " +"Click"+m_Target.BindDatas[i].Name + "Event()"+"{}");
                    }
                    if (m_Target.BindDatas[i].BindCom.GetType() == typeof(Toggle))
                    {
                        sw.WriteLine("\t\tprivate void "+"Click" + m_Target.BindDatas[i].Name + "Event(bool ison)"+"{}");
                    }
                }
                #endregion
                sw.WriteLine(scriptEnd);
                sw.WriteLine(scriptEndK);
                sw.Close();
                Debug.Log($"ClassName:{className}代码生成完毕");
            }
        }
        else
        {
            bool stopWriteMember = false;

            bool stopWrite = false;
            bool writeNewOver = false;
            string[] strArr = File.ReadAllLines(filePath);
            List<string> strList = new List<string>();
            
           
            for (int i = 0; i < strArr.Length; i++)
            {
                string str = strArr[i];
                if (str.Contains(scriptEnd))
                {
                    break;
                }
                
                if (str.Contains(btnMember))
                {
                    strList.Add(btnMember);
                    stopWriteMember = true;
                }

                if (!stopWriteMember)
                {
                    if (!strList.Contains(str))
                    strList.Add(str);
                }
                else
                {
                    if (!writeNewOver)
                    {
                        foreach (BindData data in m_Target.BindDatas)
                        {
                            if (!strList.Contains($"\t\tprivate {data.BindCom.GetType().Name} m_{data.Name};"))
                            {
                                strList.Add($"\t\tprivate {data.BindCom.GetType().Name} m_{data.Name};");
                            }
                        }
                    }
                }
                
                if (str.Contains(btnStart))
                {
                    strList.Add(btnStart);
                    stopWrite = true;
                }
                if (!stopWrite)
                {
                    if (!strList.Contains(str))
                    strList.Add(str);
                }
                else
                {
                    if (!writeNewOver)
                    {

                        if (!strList.Contains($"\t\t\tComponentAutoBindTool autoBindTool = gameObject.GetComponent<ComponentAutoBindTool>();"))
                        {
                            strList.Add($"\t\t\tComponentAutoBindTool autoBindTool = gameObject.GetComponent<ComponentAutoBindTool>();");
                        }

                        //根据索引获取
                        for (int k = 0; k < m_Target.BindDatas.Count; k++)
                        {
                            BindData data = m_Target.BindDatas[k]; 
                            string filedName = $"m_{data.Name}";
                            if (!strList.Contains($"{filedName} = autoBindTool.GetBindComponent<{data.BindCom.GetType().Name}>({k});")) 
                                strList.Add($"\t\t\t{filedName} = autoBindTool.GetBindComponent<{data.BindCom.GetType().Name}>({k});");
                        }
                        foreach (var clickFunc in clickFuncDict)
                        {
                            if (clickFunc.Value.Contains("Button")&&!strList.Contains($"\t\t\t{clickFunc.Key}.onClick.AddListener(Click{clickFunc.Value.Replace("Button","")});"))
                            strList.Add($"\t\t\t{clickFunc.Key}.onClick.AddListener(Click{clickFunc.Value.Replace("Button","")});");
                            else if (clickFunc.Value.Contains("Toggle")&& !strList.Contains($"\t\t\t{clickFunc.Key}.onValueChanged.AddListener((bool ison) =>{{Click{clickFunc.Value.Replace("Toggle","")}(ison);}});"))
                            {
                                strList.Add($"\t\t\t{clickFunc.Key}.onValueChanged.AddListener((bool ison) =>{{Click{clickFunc.Value.Replace("Toggle","")}(ison);}});");//
                            }
                        }
                     
                        writeNewOver = true;
                    }
                }
                if (str.Contains(btnEnd))
                {
                    strList.Add(btnEnd);
                    stopWrite = false;
                }
            }
            foreach (KeyValuePair<string, string> pair in clickFuncDict)
            {
                bool contain = false;
                for (int kIndex = 0; kIndex < strList.Count; kIndex++)
                {
                    string str = strList[kIndex];

                    if (pair.Value.Contains("Button"))
                    {
                        if (str.Contains($"{ pair.Value.Replace("Button","")}()"))
                        {
                            contain = true;
                            break;
                        }
                    }
                    if (pair.Value.Contains("Toggle"))
                    {
                        //string str2 = pair.Value.Replace("Toggle","")+"()";
                        if (str.Contains($"{ pair.Value.Replace("Toggle","")}(bool ison)"))
                        {
                            contain = true;
                            break;
                        }
                    }
                }
                if (!contain)
                {
                    if (pair.Value.Contains("Button"))
                    {
                        if (!strList.Contains($"\t\tprivate void Click{clickFuncDict[pair.Key]}()" + "{}"))
                            strList.Add($"\t\tprivate void Click{clickFuncDict[pair.Key].Replace("Button","")}()" + "{}");
                    }
                    if (pair.Value.Contains("Toggle"))
                    {
                        if (!strList.Contains($"\t\tprivate void Click{clickFuncDict[pair.Key]}(bool ison)" + "{}"))
                            strList.Add($"\t\tprivate void Click{clickFuncDict[pair.Key].Replace("Toggle","")}(bool ison)" + "{}");
                    }
                }
            }
            strList.Add(scriptEnd);
            strList.Add(scriptEndK);
            File.WriteAllLines(filePath, strList.ToArray());
        }

        ModifyFileFormat(filePath);
    }

    private string GetClassFolderName(string className)
    {
        string folderName = className.Replace("Form", "");
        if (className.Contains("_"))
        {
            string[] args = className.Split('_');
            if (args.Length > 1)
            {
                folderName = args[0];
            }
        }

        return folderName;
    }

    /// <summary>
    /// Get a type with name.
    /// 根据名字获取一个类型
    /// </summary>
    private Type GetTypeWithName(string typeName,string nameSpace)
    {
        Assembly[] assmblies = AppDomain.CurrentDomain.GetAssemblies();

        for (int i = assmblies.Length - 1; i >= 0; i--)
        {
            bool isFind = false;
            foreach (var assemblyName in m_Setting.MountScriptListAssemblys)
            {
                if (assemblyName == assmblies[i].GetName().Name)
                {
                    isFind = true;
                }
            }
            if (!isFind)
            {
                continue;
            }
            Type[] __types = assmblies[i].GetTypes();

            for (int j = __types.Length - 1; j >= 0; j--)
            {
                if (__types[j].Name != typeName) continue;
                if (__types[j].Namespace!= nameSpace) continue;
                return __types[j];
            }
        }
        return null;
    }
}
 