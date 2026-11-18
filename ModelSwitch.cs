using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace ChangWorkModelSwitch {
 static class App {
  [STAThread] static void Main() {
   Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
   Application.Run(new MainForm());
  }
 }

 class ConfigStore {
  public string PathName { get; private set; }
  public Dictionary<string,object> Root { get; private set; }
  readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
  public ConfigStore(string path) { PathName=path; Load(); }
  public void Load() {
   if(!File.Exists(PathName)) { Root=new Dictionary<string,object>(); return; }
   var text=File.ReadAllText(PathName,Encoding.UTF8);
   Root=string.IsNullOrWhiteSpace(text)?new Dictionary<string,object>():json.Deserialize<Dictionary<string,object>>(text);
  }
  Dictionary<string,object> Obj(Dictionary<string,object> parent,string key) {
   object v; Dictionary<string,object> d;
   if(parent.TryGetValue(key,out v) && (d=v as Dictionary<string,object>)!=null)return d;
   d=new Dictionary<string,object>(); parent[key]=d; return d;
  }
  public Dictionary<string,object> Providers { get { return Obj(Obj(Root,"models"),"providers"); } }
  public string Backup() {
   if(!File.Exists(PathName))return "";
   var dir=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(PathName),"model-switch-backups");Directory.CreateDirectory(dir);
   var p=System.IO.Path.Combine(dir,"cclaw-assist."+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");File.Copy(PathName,p,true);return p;
  }
  public void Save() {
   Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)); Backup();
   var tmp=PathName+".tmp-"+Guid.NewGuid().ToString("N");File.WriteAllText(tmp,PrettyJson(Root),new UTF8Encoding(false));
   json.DeserializeObject(File.ReadAllText(tmp));File.Copy(tmp,PathName,true);File.Delete(tmp);
  }
  string PrettyJson(object o) { return json.Serialize(o); }
  public List<string> ProviderIds(){return Providers.Keys.OrderBy(x=>x).ToList();}
  public Dictionary<string,object> Provider(string id){object v;return Providers.TryGetValue(id,out v)?v as Dictionary<string,object>:null;}
  public static List<object> AsList(object value) {
   var result=new List<object>(); if(value==null)return result;
   var enumerable=value as IEnumerable; if(enumerable==null||value is string)return result;
   foreach(var x in enumerable)result.Add(x); return result;
  }
  public string DefaultModel(string agentId) {
   object av,lv;if(!Root.TryGetValue("agents",out av))return "";var a=av as Dictionary<string,object>;if(a==null||!a.TryGetValue("list",out lv))return "";
   foreach(var x in AsList(lv)){var d=x as Dictionary<string,object>;object id,m;if(d!=null&&d.TryGetValue("id",out id)&&Convert.ToString(id)==agentId&&d.TryGetValue("model",out m)){if(m is string)return (string)m;var md=m as Dictionary<string,object>;object p;if(md!=null&&md.TryGetValue("primary",out p))return Convert.ToString(p);}} return "";
  }
  public List<string> AgentIds(){var r=new List<string>();object av,lv;if(Root.TryGetValue("agents",out av)){var a=av as Dictionary<string,object>;if(a!=null&&a.TryGetValue("list",out lv))foreach(var x in AsList(lv)){var d=x as Dictionary<string,object>;object id;if(d!=null&&d.TryGetValue("id",out id))r.Add(Convert.ToString(id));}}if(r.Count==0)r.Add("main");return r;}
  public void SetDefault(string agentId,string full) {
   var agents=Obj(Root,"agents");object lv;var arr=agents.TryGetValue("list",out lv)?AsList(lv):new List<object>();
   Dictionary<string,object> found=null;foreach(var x in arr){var d=x as Dictionary<string,object>;object id;if(d!=null&&d.TryGetValue("id",out id)&&Convert.ToString(id)==agentId){found=d;break;}}
   if(found==null){found=new Dictionary<string,object>{{"id",agentId},{"default",arr.Count==0}};arr.Add(found);}found["model"]=new Dictionary<string,object>{{"primary",full},{"fallbacks",new object[0]}};agents["list"]=arr.ToArray();
  }
 }

 class MainForm:Form {
  readonly Color Ink=Color.FromArgb(23,25,29), Accent=Color.FromArgb(18,165,148), Bg=Color.FromArgb(245,246,248), Muted=Color.FromArgb(110,116,126);
  ConfigStore store; string configPath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".cclaw-assist","cclaw-assist.json");
  ListBox providers=new ListBox(),models=new ListBox(),backups=new ListBox(); ComboBox agents=new ComboBox(),adapter=new ComboBox(),secretMode=new ComboBox(); TextBox id=new TextBox(),url=new TextBox(),envName=new TextBox(),key=new TextBox(); Label current=new Label(),status=new Label(),title=new Label(); Panel content=new Panel(); Button add=new Button(); string selected="";
  public MainForm(){Text="Model Switch for 畅Work";AutoScaleMode=AutoScaleMode.Dpi;Width=1180;Height=760;MinimumSize=new Size(1040,680);StartPosition=FormStartPosition.CenterScreen;BackColor=Bg;Font=new Font("Microsoft YaHei UI",9);Build();Shown+=(s,e)=>LoadAll();}
  Button Btn(string text,Color back,Color fore){var b=new Button{Text=text,BackColor=back,ForeColor=fore,FlatStyle=FlatStyle.Flat,Height=38,Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=0;return b;}
  Label L(string text,int size=9,bool bold=false){return new Label{Text=text,AutoSize=true,ForeColor=Ink,Font=new Font(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular)};}
  void Build(){
   var side=new Panel{Dock=DockStyle.Left,Width=220,BackColor=Ink,Padding=new Padding(20)};Controls.Add(side);
   var logo=L("M",22,true);logo.ForeColor=Color.White;logo.Location=new Point(18,20);side.Controls.Add(logo);var brand=L("模型切换",16,true);brand.ForeColor=Color.White;brand.Location=new Point(18,65);side.Controls.Add(brand);var sub=L("畅Work / OpenClaw",9);sub.ForeColor=Color.FromArgb(145,150,160);sub.Location=new Point(18,94);side.Controls.Add(sub);
   var nav1=Btn("▦   提供商",Color.FromArgb(43,46,52),Color.White);nav1.SetBounds(16,145,188,42);side.Controls.Add(nav1);var nav2=Btn("↶   备份与迁移",Ink,Color.Gainsboro);nav2.SetBounds(16,195,188,42);side.Controls.Add(nav2);
   var folder=Btn("打开配置目录",Color.FromArgb(37,40,46),Color.Gainsboro);folder.SetBounds(16,650,188,38);folder.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom;folder.Click+=(s,e)=>{Directory.CreateDirectory(System.IO.Path.GetDirectoryName(configPath));Process.Start("explorer.exe",System.IO.Path.GetDirectoryName(configPath));};side.Controls.Add(folder);
   var main=new Panel{Padding=new Padding(30,24,30,24)};main.SetBounds(220,0,ClientSize.Width-220,ClientSize.Height);main.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;Controls.Add(main);side.BringToFront();title=L("提供商",26,true);title.Location=new Point(30,20);main.Controls.Add(title);var desc=L("管理模型端点，并为 Agent 一键切换默认模型",10);desc.ForeColor=Muted;desc.Location=new Point(32,60);main.Controls.Add(desc);add=Btn("＋ 新增提供商",Accent,Color.White);add.SetBounds(760,22,145,40);add.Anchor=AnchorStyles.Top|AnchorStyles.Right;add.Click+=(s,e)=>NewProvider();main.Controls.Add(add);
   content.SetBounds(30,95,main.ClientSize.Width-60,main.ClientSize.Height-125);content.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;main.Controls.Add(content);nav1.Click+=(s,e)=>ShowProviders();nav2.Click+=(s,e)=>ShowBackups();ShowProviders();
  }
  Panel Card(){return new Panel{BackColor=Color.White,Padding=new Padding(18)};}
  void ShowProviders(){content.Controls.Clear();title.Text="提供商";add.Visible=true;var left=Card();left.SetBounds(0,0,340,content.Height);left.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left;content.Controls.Add(left);
   var hero=new Panel{BackColor=Ink};hero.SetBounds(0,0,340,88);left.Controls.Add(hero);var x=L("当前默认模型",9);x.ForeColor=Color.Silver;x.Location=new Point(16,10);hero.Controls.Add(x);current=L("未设置",10,true);current.ForeColor=Color.White;current.SetBounds(16,35,308,22);current.AutoSize=false;current.AutoEllipsis=true;hero.Controls.Add(current);agents.SetBounds(16,59,120,24);agents.DropDownStyle=ComboBoxStyle.DropDownList;agents.SelectedIndexChanged+=(s,e)=>UpdateCurrent();hero.Controls.Add(agents);
   providers.SetBounds(0,102,340,left.Height-102);providers.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;providers.BorderStyle=BorderStyle.None;providers.Font=new Font(Font.FontFamily,9);providers.ItemHeight=42;providers.HorizontalScrollbar=true;providers.SelectedIndexChanged+=(s,e)=>SelectProvider();left.Controls.Add(providers);
   var edit=Card();edit.SetBounds(360,0,content.Width-360,content.Height);edit.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;content.Controls.Add(edit);BuildEditor(edit);if(store!=null)RefreshLists();}
  void BuildEditor(Panel p){
   p.Padding=new Padding(20);p.AutoScroll=false;
   var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=7,BackColor=Color.White,Margin=new Padding(0),Padding=new Padding(0)};
   grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
   grid.RowStyles.Add(new RowStyle(SizeType.Absolute,52));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,235));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,45));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,28));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,48));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,0));p.Controls.Add(grid);

   var header=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0)};grid.Controls.Add(header,0,0);grid.SetColumnSpan(header,2);var et=L("选择或新增提供商",18,true);et.Location=new Point(0,7);header.Controls.Add(et);var save=Btn("保存",Accent,Color.White);save.SetBounds(header.Width-90,2,90,38);save.Anchor=AnchorStyles.Top|AnchorStyles.Right;save.Click+=(s,e)=>SaveProvider();header.Controls.Add(save);var del=Btn("删除",Color.FromArgb(253,235,236),Color.FromArgb(180,50,60));del.SetBounds(header.Width-190,2,90,38);del.Anchor=AnchorStyles.Top|AnchorStyles.Right;del.Click+=(s,e)=>DeleteProvider();header.Controls.Add(del);

   var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Margin=new Padding(0),Padding=new Padding(0)};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));fields.RowStyles.Add(new RowStyle(SizeType.Absolute,58));fields.RowStyles.Add(new RowStyle(SizeType.Absolute,58));fields.RowStyles.Add(new RowStyle(SizeType.Absolute,58));fields.RowStyles.Add(new RowStyle(SizeType.Absolute,58));grid.Controls.Add(fields,0,1);grid.SetColumnSpan(fields,2);
   fields.Controls.Add(FieldCell("提供商 ID",id,new Padding(0,0,10,5)),0,0);fields.Controls.Add(FieldCell("API 适配器",adapter,new Padding(10,0,0,5)),1,0);
   if(adapter.Items.Count==0)adapter.Items.AddRange(new object[]{"openai-completions","openai-responses","openai-chatgpt-responses","anthropic-messages","google-generative-ai","google-vertex","ollama","azure-openai-responses"});if(adapter.SelectedIndex<0)adapter.SelectedIndex=0;
   fields.Controls.Add(FieldCell("Base URL",url,new Padding(0,0,0,5)),0,1);fields.SetColumnSpan(fields.GetControlFromPosition(0,1),2);
   if(secretMode.Items.Count==0)secretMode.Items.AddRange(new object[]{"保持原值","环境变量（推荐）","直接写入配置","不设置密钥"});if(secretMode.SelectedIndex<0)secretMode.SelectedIndex=0;
   fields.Controls.Add(FieldCell("密钥方式",secretMode,new Padding(0,0,10,0)),0,2);fields.Controls.Add(FieldCell("环境变量名",envName,new Padding(10,0,0,0)),1,2);if(string.IsNullOrEmpty(envName.Text))envName.Text="CUSTOM_MODEL_API_KEY";
   fields.Controls.Add(FieldCell("API Key（留空保留原值）",key,new Padding(0,0,0,0)),0,3);fields.SetColumnSpan(fields.GetControlFromPosition(0,3),2);key.UseSystemPasswordChar=true;

   var modelHead=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0)};grid.Controls.Add(modelHead,0,2);grid.SetColumnSpan(modelHead,2);var ml=L("模型列表",10,true);ml.Location=new Point(0,2);modelHead.Controls.Add(ml);var mh=L("可从上游自动同步，也可以手动添加。",8);mh.ForeColor=Muted;mh.Location=new Point(0,23);modelHead.Controls.Add(mh);var fetch=Btn("↻ 获取上游模型",Color.FromArgb(225,247,243),Accent);fetch.SetBounds(modelHead.Width-145,4,145,34);fetch.Anchor=AnchorStyles.Top|AnchorStyles.Right;fetch.Click+=async(s,e)=>await FetchUpstreamModels(fetch);modelHead.Controls.Add(fetch);
   models.Dock=DockStyle.Fill;models.Margin=new Padding(0);grid.Controls.Add(models,0,3);grid.SetColumnSpan(models,2);
   status=L("就绪",8);status.ForeColor=Muted;status.AutoSize=false;status.Dock=DockStyle.Fill;status.AutoEllipsis=true;status.TextAlign=ContentAlignment.MiddleLeft;status.Margin=new Padding(0);grid.Controls.Add(status,0,4);grid.SetColumnSpan(status,2);
   var actions=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0)};grid.Controls.Add(actions,0,5);grid.SetColumnSpan(actions,2);var addm=Btn("＋ 添加模型",Color.FromArgb(233,235,239),Ink);addm.SetBounds(0,5,110,38);addm.Click+=(s,e)=>EditModel(-1);actions.Controls.Add(addm);var editm=Btn("编辑模型",Color.FromArgb(233,235,239),Ink);editm.SetBounds(118,5,100,38);editm.Click+=(s,e)=>EditModel(models.SelectedIndex);actions.Controls.Add(editm);var rem=Btn("移除",Color.FromArgb(253,235,236),Color.FromArgb(180,50,60));rem.SetBounds(226,5,80,38);rem.Click+=(s,e)=>{if(models.SelectedIndex>=0)models.Items.RemoveAt(models.SelectedIndex);};actions.Controls.Add(rem);var activate=Btn("启用选中模型",Accent,Color.White);activate.SetBounds(actions.Width-140,5,140,38);activate.Anchor=AnchorStyles.Top|AnchorStyles.Right;activate.Click+=(s,e)=>ActivateSelectedModel();actions.Controls.Add(activate);
  }
  Panel FieldCell(string caption,Control input,Padding margin){var cell=new Panel{Dock=DockStyle.Fill,Margin=margin};var label=L(caption,9,true);label.SetBounds(2,0,cell.Width-4,20);label.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;cell.Controls.Add(label);input.SetBounds(0,24,cell.Width,29);input.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;cell.Controls.Add(input);return cell;}
  void AddField(Control p,string label,Control c,int x,int y,int w){var l=L(label,9,true);l.Location=new Point(x,y);p.Controls.Add(l);c.SetBounds(x,y+24,w,31);p.Controls.Add(c);}
  void LoadAll(){try{store=new ConfigStore(configPath);RefreshLists();}catch(Exception ex){MessageBox.Show(ex.Message,"加载失败",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
  string CompactId(string value,int max){if(string.IsNullOrEmpty(value)||value.Length<=max)return value;int left=(max-1)/2;int right=max-1-left;return value.Substring(0,left)+"…"+value.Substring(value.Length-right);}
  void RefreshLists(){if(providers==null||agents==null)return;providers.BeginUpdate();providers.Items.Clear();foreach(var x in store.ProviderIds()){var p=store.Provider(x);object mv;int n=p!=null&&p.TryGetValue("models",out mv)?ConfigStore.AsList(mv).Count:0;providers.Items.Add(CompactId(x,22)+"    "+n+" 个模型");}providers.EndUpdate();agents.Items.Clear();foreach(var a in store.AgentIds())agents.Items.Add(a);if(agents.Items.Count>0)agents.SelectedIndex=0;UpdateCurrent();}
  void UpdateCurrent(){if(store!=null&&agents.SelectedItem!=null){var value=store.DefaultModel(agents.Text);var shown=value;if(!string.IsNullOrEmpty(value)&&value.Contains("/"))shown=value.Substring(value.LastIndexOf('/')+1);current.Text=string.IsNullOrEmpty(value)?"未设置":CompactId(shown,30);current.Tag=value;var tip=new ToolTip();tip.SetToolTip(current,value);}}
  void NewProvider(){selected="";id.ReadOnly=false;id.Text="";url.Text="";key.Text="";envName.Text="CUSTOM_MODEL_API_KEY";adapter.SelectedIndex=0;secretMode.SelectedIndex=1;models.Items.Clear();id.Focus();}
  void SelectProvider(){if(providers.SelectedIndex<0||store==null)return;var ids=store.ProviderIds();if(providers.SelectedIndex>=ids.Count)return;selected=ids[providers.SelectedIndex];var p=store.Provider(selected);id.Text=selected;id.ReadOnly=true;object v;url.Text=p.TryGetValue("baseUrl",out v)?Convert.ToString(v):"";adapter.SelectedItem=p.TryGetValue("api",out v)?Convert.ToString(v):"openai-completions";secretMode.SelectedIndex=0;envName.Text="CUSTOM_MODEL_API_KEY";if(p.TryGetValue("apiKey",out v)){var d=v as Dictionary<string,object>;object z;if(d!=null&&d.TryGetValue("source",out z)&&Convert.ToString(z)=="env"){secretMode.SelectedIndex=1;if(d.TryGetValue("id",out z))envName.Text=Convert.ToString(z);}}key.Text="";models.Items.Clear();if(p.TryGetValue("models",out v))foreach(var o in ConfigStore.AsList(v)){var m=o as Dictionary<string,object>;if(m!=null)models.Items.Add(ModelLine(m));}}
  string ModelLine(Dictionary<string,object> m){object v;string mid=m.TryGetValue("id",out v)?Convert.ToString(v):"";string name=m.TryGetValue("name",out v)?Convert.ToString(v):mid;string ctx=m.TryGetValue("contextWindow",out v)?Convert.ToString(v):"";string max=m.TryGetValue("maxTokens",out v)?Convert.ToString(v):"";string rea=m.TryGetValue("reasoning",out v)?Convert.ToString(v).ToLower():"";return mid+" | "+name+" | "+ctx+" | "+max+" | "+rea;}
  Dictionary<string,object> ParseModel(string line){var q=line.Split('|').Select(x=>x.Trim()).ToArray();var m=new Dictionary<string,object>{{"id",q[0]},{"name",q.Length>1&&!string.IsNullOrEmpty(q[1])?q[1]:q[0]}};long n;if(q.Length>2&&long.TryParse(q[2],out n))m["contextWindow"]=n;if(q.Length>3&&long.TryParse(q[3],out n))m["maxTokens"]=n;bool b;if(q.Length>4&&bool.TryParse(q[4],out b))m["reasoning"]=b;return m;}
  void EditModel(int index){using(var f=new ModelDialog(index>=0?Convert.ToString(models.Items[index]):"")){if(f.ShowDialog(this)==DialogResult.OK){if(index>=0)models.Items[index]=f.Value;else models.Items.Add(f.Value);}}}
  string ResolveApiKey(){
   if(!string.IsNullOrWhiteSpace(key.Text))return key.Text.Trim();
   if(secretMode.SelectedIndex==1&&!string.IsNullOrWhiteSpace(envName.Text)){var v=Environment.GetEnvironmentVariable(envName.Text.Trim(),EnvironmentVariableTarget.User);if(string.IsNullOrEmpty(v))v=Environment.GetEnvironmentVariable(envName.Text.Trim());return v??"";}
   if(secretMode.SelectedIndex==0&&!string.IsNullOrEmpty(selected)){var p=store.Provider(selected);object v;if(p!=null&&p.TryGetValue("apiKey",out v)){if(v is string)return Convert.ToString(v);var d=v as Dictionary<string,object>;object sid;if(d!=null&&d.TryGetValue("id",out sid)){var n=Convert.ToString(sid);var z=Environment.GetEnvironmentVariable(n,EnvironmentVariableTarget.User);if(string.IsNullOrEmpty(z))z=Environment.GetEnvironmentVariable(n);return z??"";}}}return "";
  }
  string ModelsEndpoint(){var raw=url.Text.Trim().TrimEnd('/');if(string.IsNullOrEmpty(raw))throw new Exception("请先填写 Base URL。");if(raw.EndsWith("/models",StringComparison.OrdinalIgnoreCase))return raw;return raw+"/models";}
  async Task FetchUpstreamModels(Button button){
   button.Enabled=false;var old=button.Text;button.Text="正在同步…";status.Text="正在连接上游模型接口";
   try{
    ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;var endpoint=ModelsEndpoint();var token=ResolveApiKey();
    using(var client=new HttpClient()){
     client.Timeout=TimeSpan.FromSeconds(30);client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
     if(!string.IsNullOrEmpty(token))client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
     if(adapter.Text.StartsWith("anthropic")&&!string.IsNullOrEmpty(token)){client.DefaultRequestHeaders.Authorization=null;client.DefaultRequestHeaders.Add("x-api-key",token);client.DefaultRequestHeaders.Add("anthropic-version","2023-06-01");}
     var response=await client.GetAsync(endpoint);var body=await response.Content.ReadAsStringAsync();
     if(!response.IsSuccessStatusCode)throw new Exception("上游返回 "+(int)response.StatusCode+" "+response.ReasonPhrase+"\r\n"+ShortText(body,500));
     var ids=ParseUpstreamModelIds(body);if(ids.Count==0)throw new Exception("接口请求成功，但没有识别到模型。为防止误删，本次不会修改本地列表。");
     var upstream=new HashSet<string>(ids,StringComparer.OrdinalIgnoreCase);
     var localLines=models.Items.Cast<object>().Select(x=>Convert.ToString(x)).ToList();
     var removed=localLines.Where(x=>!upstream.Contains(ModelIdFromLine(x))).ToList();
     var existing=new HashSet<string>(localLines.Select(ModelIdFromLine),StringComparer.OrdinalIgnoreCase);
     var addedCandidates=ids.Where(x=>!existing.Contains(x)).ToList();
     var invalidDefaults=FindDefaultsUsing(removed.Select(ModelIdFromLine));
     if(removed.Count>0){
      var message="上游已不再返回 "+removed.Count+" 个本地模型，继续后会把它们从当前列表移除：\r\n\r\n"+string.Join("\r\n",removed.Select(ModelIdFromLine).Take(15));
      if(removed.Count>15)message+="\r\n…以及另外 "+(removed.Count-15)+" 个";
      if(invalidDefaults.Count>0)message+="\r\n\r\n警告：以下 Agent 的默认模型也已失效：\r\n"+string.Join("\r\n",invalidDefaults);
      message+="\r\n\r\n删除仅在点击主界面“保存”后写入配置。是否继续同步？";
      if(MessageBox.Show(message,"确认移除上游已下线模型",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes){status.Text="已取消同步，本地列表未修改";return;}
     }
     foreach(var line in removed)models.Items.Remove(line);
     int added=0;
     if(addedCandidates.Count>0){
      using(var picker=new UpstreamModelsDialog(addedCandidates)){if(picker.ShowDialog(this)==DialogResult.OK){foreach(var mid in picker.SelectedModels){models.Items.Add(mid+" | "+mid+" | 128000 | 16000 | false");added++;}}}
     }
     status.Text="同步完成：新增 "+added+"，移除 "+removed.Count+"（点击保存后生效）";
     if(invalidDefaults.Count>0)MessageBox.Show("同步后的列表中，以下 Agent 默认模型已经失效：\r\n\r\n"+string.Join("\r\n",invalidDefaults)+"\r\n\r\n请重新选择有效模型并保存。","默认模型已失效",MessageBoxButtons.OK,MessageBoxIcon.Warning);
     else if(addedCandidates.Count==0&&removed.Count==0)MessageBox.Show("本地模型列表与上游一致，无需更新。","同步完成",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
   }catch(Exception ex){MessageBox.Show(ex.Message,"同步上游模型失败",MessageBoxButtons.OK,MessageBoxIcon.Error);status.Text="同步失败，本地列表未修改";}finally{button.Text=old;button.Enabled=true;}
  }
  string ModelIdFromLine(string line){return (line??"").Split('|')[0].Trim();}
  List<string> FindDefaultsUsing(IEnumerable<string> removedIds){
   var removed=new HashSet<string>(removedIds,StringComparer.OrdinalIgnoreCase);var result=new List<string>();var pid=id.Text.Trim();
   foreach(var agent in store.AgentIds()){var full=store.DefaultModel(agent);var prefix=pid+"/";if(full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&removed.Contains(full.Substring(prefix.Length)))result.Add(agent+" → "+full);}
   return result;
  }
  string ShortText(string text,int max){if(string.IsNullOrEmpty(text))return "";text=text.Replace("\r"," ").Replace("\n"," ");return text.Length<=max?text:text.Substring(0,max)+"…";}
  List<string> ParseUpstreamModelIds(string body){var result=new List<string>();var js=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};var root=js.DeserializeObject(body);CollectModelIds(root,result);return result.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();}
  void CollectModelIds(object node,List<string> result){var d=node as Dictionary<string,object>;if(d!=null){object list;if(d.TryGetValue("data",out list)||d.TryGetValue("models",out list)){foreach(var x in ConfigStore.AsList(list)){var md=x as Dictionary<string,object>;object idv;if(md!=null&&md.TryGetValue("id",out idv))result.Add(Convert.ToString(idv));else if(x is string)result.Add(Convert.ToString(x));}}else{object idv;if(d.TryGetValue("id",out idv))result.Add(Convert.ToString(idv));}return;}foreach(var x in ConfigStore.AsList(node)){var md=x as Dictionary<string,object>;object idv;if(md!=null&&md.TryGetValue("id",out idv))result.Add(Convert.ToString(idv));else if(x is string)result.Add(Convert.ToString(x));}}
  void SaveProvider(){try{var pid=id.Text.Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(pid,"^[a-z][a-z0-9_-]{0,63}$"))throw new Exception("提供商 ID 格式不正确。");Uri u;if(!Uri.TryCreate(url.Text.Trim(),UriKind.Absolute,out u))throw new Exception("Base URL 不正确。");if(models.Items.Count==0)throw new Exception("至少添加一个模型。");var p=new Dictionary<string,object>{{"baseUrl",url.Text.TrimEnd('/')},{"api",adapter.Text},{"models",models.Items.Cast<object>().Select(x=>(object)ParseModel(Convert.ToString(x))).ToArray()}};var old=store.Provider(pid);if(secretMode.SelectedIndex==0&&old!=null&&old.ContainsKey("apiKey"))p["apiKey"]=old["apiKey"];else if(secretMode.SelectedIndex==1){if(string.IsNullOrWhiteSpace(envName.Text))throw new Exception("请输入环境变量名。");if(!string.IsNullOrEmpty(key.Text))Environment.SetEnvironmentVariable(envName.Text,key.Text,EnvironmentVariableTarget.User);p["apiKey"]=new Dictionary<string,object>{{"source","env"},{"provider","default"},{"id",envName.Text}};}else if(secretMode.SelectedIndex==2){if(string.IsNullOrEmpty(key.Text))throw new Exception("请输入 API Key。");p["apiKey"]=key.Text;}store.Providers[pid]=p;var modelRoot=(Dictionary<string,object>)store.Root["models"];modelRoot["mode"]="merge";store.Save();selected=pid;key.Text="";RefreshLists();status.Text="保存成功，已自动备份";}catch(Exception ex){MessageBox.Show(ex.Message,"保存失败",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
  void DeleteProvider(){if(string.IsNullOrEmpty(selected))return;if(MessageBox.Show("确定删除 "+selected+"？","确认",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){store.Providers.Remove(selected);store.Save();NewProvider();RefreshLists();}}
  void ActivateSelectedModel(){if(models.SelectedIndex<0){MessageBox.Show("请先选择一个模型。");return;}var m=ParseModel(Convert.ToString(models.SelectedItem));store.SetDefault(agents.Text,id.Text.Trim()+"/"+m["id"]);store.Save();UpdateCurrent();status.Text="已切换默认模型；重启畅Work后完全生效";}
  void ShowBackups(){content.Controls.Clear();title.Text="备份与迁移";add.Visible=false;var p=Card();p.Dock=DockStyle.Fill;content.Controls.Add(p);var h=L("自动备份",18,true);h.Location=new Point(18,15);p.Controls.Add(h);backups.SetBounds(18,58,p.Width-380,p.Height-80);backups.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;p.Controls.Add(backups);LoadBackups();var restore=Btn("恢复选中备份",Color.FromArgb(253,235,236),Color.FromArgb(180,50,60));restore.SetBounds(p.Width-330,60,280,40);restore.Anchor=AnchorStyles.Top|AnchorStyles.Right;restore.Click+=(s,e)=>Restore();p.Controls.Add(restore);var exp=Btn("导出提供商配置",Accent,Color.White);exp.SetBounds(p.Width-330,120,280,40);exp.Anchor=AnchorStyles.Top|AnchorStyles.Right;exp.Click+=(s,e)=>Export();p.Controls.Add(exp);var imp=Btn("导入提供商配置",Color.FromArgb(233,235,239),Ink);imp.SetBounds(p.Width-330,170,280,40);imp.Anchor=AnchorStyles.Top|AnchorStyles.Right;imp.Click+=(s,e)=>Import();p.Controls.Add(imp);}
  void LoadBackups(){backups.Items.Clear();var dir=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(configPath),"model-switch-backups");if(Directory.Exists(dir))foreach(var f in new DirectoryInfo(dir).GetFiles("cclaw-assist.*.json").OrderByDescending(x=>x.LastWriteTime))backups.Items.Add(f.FullName);}
  void Restore(){if(backups.SelectedItem==null)return;if(MessageBox.Show("确定恢复所选备份？当前配置也会先备份。","确认",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){store.Backup();File.Copy(Convert.ToString(backups.SelectedItem),configPath,true);LoadAll();MessageBox.Show("恢复完成。");}}
  void Export(){var d=new SaveFileDialog{Filter="JSON 配置|*.json",FileName="openclaw-providers.json"};if(d.ShowDialog()==DialogResult.OK){var wrap=new Dictionary<string,object>{{"format","openclaw-model-switch"},{"version",1},{"providers",store.Providers}};var js=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};File.WriteAllText(d.FileName,js.Serialize(wrap),new UTF8Encoding(false));MessageBox.Show("导出完成。注意：当前版本导出包含配置中的密钥引用；若是明文密钥，请妥善保管文件。");}}
  void Import(){var d=new OpenFileDialog{Filter="JSON 配置|*.json"};if(d.ShowDialog()==DialogResult.OK){try{var js=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};var w=js.Deserialize<Dictionary<string,object>>(File.ReadAllText(d.FileName));var ps=(Dictionary<string,object>)w["providers"];foreach(var x in ps)store.Providers[x.Key]=x.Value;store.Save();RefreshLists();MessageBox.Show("导入完成。");}catch(Exception ex){MessageBox.Show(ex.Message,"导入失败");}}}
 }
 class UpstreamModelsDialog:Form {
  CheckedListBox list=new CheckedListBox(); TextBox search=new TextBox(); List<string> all; public List<string> SelectedModels{get;private set;}
  public UpstreamModelsDialog(List<string> items){all=items;SelectedModels=new List<string>();Text="选择上游模型";Width=570;Height=610;MinimumSize=new Size(470,450);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",9);var label=new Label{Text="上游返回 "+items.Count+" 个模型。勾选需要导入的模型：",AutoSize=true,Location=new Point(18,18)};Controls.Add(label);search.SetBounds(18,45,520,31);search.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;search.TextChanged+=(s,e)=>Fill(search.Text);Controls.Add(search);list.CheckOnClick=true;list.HorizontalScrollbar=true;list.SetBounds(18,84,520,430);list.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;Controls.Add(list);var allBtn=new Button{Text="全选"};allBtn.SetBounds(18,525,80,34);allBtn.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;allBtn.Click+=(s,e)=>{for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,true);};Controls.Add(allBtn);var none=new Button{Text="全不选"};none.SetBounds(105,525,80,34);none.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;none.Click+=(s,e)=>{for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,false);};Controls.Add(none);var ok=new Button{Text="导入选中模型",DialogResult=DialogResult.OK};ok.SetBounds(398,525,140,34);ok.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;ok.Click+=(s,e)=>{SelectedModels=list.CheckedItems.Cast<object>().Select(x=>Convert.ToString(x)).ToList();if(SelectedModels.Count==0){MessageBox.Show("请至少勾选一个模型。");DialogResult=DialogResult.None;}};Controls.Add(ok);AcceptButton=ok;Fill("");}
  void Fill(string q){var checkedSet=new HashSet<string>(list.CheckedItems.Cast<object>().Select(x=>Convert.ToString(x)),StringComparer.OrdinalIgnoreCase);list.BeginUpdate();list.Items.Clear();foreach(var x in all.Where(x=>string.IsNullOrWhiteSpace(q)||x.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0))list.Items.Add(x,checkedSet.Contains(x));list.EndUpdate();}
 }
 class ModelDialog:Form {public string Value{get;private set;} TextBox id=new TextBox(),name=new TextBox(),ctx=new TextBox(),max=new TextBox();CheckBox reasoning=new CheckBox();public ModelDialog(string value){Text="模型";Width=430;Height=350;StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",9);Field("模型 ID",id,20);Field("显示名",name,75);Field("上下文窗口",ctx,130);Field("maxTokens",max,185);reasoning.Text="推理模型";reasoning.SetBounds(20,240,120,25);Controls.Add(reasoning);var ok=new Button{Text="确定",DialogResult=DialogResult.OK};ok.SetBounds(290,255,90,36);ok.Click+=(s,e)=>{if(string.IsNullOrWhiteSpace(id.Text)){MessageBox.Show("模型 ID 不能为空");DialogResult=DialogResult.None;return;}Value=string.Join(" | ",new[]{id.Text.Trim(),string.IsNullOrWhiteSpace(name.Text)?id.Text.Trim():name.Text.Trim(),ctx.Text.Trim(),max.Text.Trim(),reasoning.Checked.ToString().ToLower()});};Controls.Add(ok);AcceptButton=ok;if(!string.IsNullOrEmpty(value)){var p=value.Split('|').Select(x=>x.Trim()).ToArray();if(p.Length>0)id.Text=p[0];if(p.Length>1)name.Text=p[1];if(p.Length>2)ctx.Text=p[2];if(p.Length>3)max.Text=p[3];bool b;if(p.Length>4&&bool.TryParse(p[4],out b))reasoning.Checked=b;}}
  void Field(string label,TextBox box,int y){var l=new Label{Text=label,AutoSize=true,Location=new Point(20,y)};box.SetBounds(150,y-4,230,30);Controls.Add(l);Controls.Add(box);}
 }
}
