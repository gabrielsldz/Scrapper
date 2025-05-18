using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

static class Const
{
    public static readonly Dictionary<int,string> REGIONS = new()
    {
        [1]="Norte",[2]="Nordeste",[3]="Sudeste",[4]="Sul",[5]="Centro-Oeste"
    };

    public static readonly Dictionary<string,string> SEXPARAM = new()
    {
        ["ALL"]="TODAS_AS_CATEGORIAS__",["M"]="Masculino%7CM%7C1",["F"]="Feminino%7CF%7C1"
    };

    public static readonly Dictionary<string,string> AGE_GROUPS = new()
    {
        ["0 a 19 anos"]="0+a+19+anos%7C000-019%7C3",
        ["20 a 24 anos"]="20+a+24+anos%7C020-024%7C3",
        ["25 a 29 anos"]="25+a+29+anos%7C025-029%7C3",
        ["30 a 34 anos"]="30+a+34+anos%7C030-034%7C3",
        ["35 a 39 anos"]="35+a+39+anos%7C035-039%7C3",
        ["40 a 44 anos"]="40+a+44+anos%7C040-044%7C3",
        ["45 a 49 anos"]="45+a+49+anos%7C045-049%7C3",
        ["50 a 54 anos"]="50+a+54+anos%7C050-054%7C3",
        ["55 a 59 anos"]="55+a+59+anos%7C055-059%7C3",
        ["60 a 64 anos"]="60+a+64+anos%7C060-064%7C3",
        ["65 a 69 anos"]="65+a+69+anos%7C065-069%7C3",
        ["70 a 74 anos"]="70+a+74+anos%7C070-074%7C3",
        ["75 a 79 anos"]="75+a+79+anos%7C075-079%7C3",
        ["80 anos e mais"]="80+anos+e+mais%7C080-999%7C3"
    };

    public static readonly List<string> CODES_DETALHADOS = BuildCidList();
    private static List<string> BuildCidList()
    {
        var l=new List<string>();
        void AddRange(IEnumerable<int> seq)=>l.AddRange(seq.Select(n=>$"C{n:00}"));
        AddRange(Enumerable.Range(0,17));
        AddRange(Enumerable.Range(17,10));
        AddRange(new[]{30,31,32,33,34,37,38,39});
        AddRange(new[]{40,41,43,44,45,46,47,48,49});
        AddRange(Enumerable.Range(50,10));
        AddRange(Enumerable.Range(60,10));
        AddRange(Enumerable.Range(70,9));
        l.AddRange(new[]
        { "C79","C80","C81","C82","C83","C84","C85","C88",
          "C90","C91","C92","C93","C94","C95","C96","C97"});
        l.AddRange(Enumerable.Range(0,8).Where(n=>n!=8).Select(n=>$"D{n:00}"));
        l.Add("D09");
        l.AddRange(Enumerable.Range(37,12).Select(n=>$"D{n:00}"));
        return l;
    }

    public const string URL_POST   ="http://tabnet.datasus.gov.br/cgi/webtabx.exe?PAINEL_ONCO/PAINEL_ONCOLOGIABR.def";
    public const string URL_COOKIE ="http://tabnet.datasus.gov.br/cgi/dhdat.exe?PAINEL_ONCO/PAINEL_ONCOLOGIABR.def";
    public const double PROGRESS_STEP = 1e-7;

    public static readonly Regex RE_ADDROWS=new(@"data\.addRows\(\s*\[(.*?)\]\s*\);",
                                                RegexOptions.Compiled|RegexOptions.Singleline);
    public static readonly Regex RE_LINHA=new(@"\[\s*['""]\s*(\d+)\s+Regi(?:ão|ao)[^]]+['""]\s*,\s*\{v:\s*([\d\.]+)",
                                              RegexOptions.Compiled);
}


record Config(
    List<int> Years,
    List<string> Faixas,
    List<string> Cids,
    int Threads,
    int Timeout,
    int Retries,
    string OutputFile);

class Program
{
    private static HttpClient http = null!;
    
    static void InitHttpClient(Config cfg)
    {
        var handler=new SocketsHttpHandler
        {
            MaxConnectionsPerServer = cfg.Threads,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        http=new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        http.DefaultRequestHeaders.Add("Origin","http://tabnet.datasus.gov.br");
        http.DefaultRequestHeaders.Referrer=new Uri(Const.URL_COOKIE);

        http.GetAsync(Const.URL_COOKIE+"?PAINEL_ONCO/PAINEL_ONCOLOGIABR.def=").Wait();
    }


    static async Task Main()
    {
        var cfg = AskUser();
        InitHttpClient(cfg);

        Console.WriteLine($"\n[onco] Anos   : {string.Join(", ", cfg.Years)}");
        Console.WriteLine($"[onco] Faixas : {cfg.Faixas.Count}");
        Console.WriteLine($"[onco] CIDs   : {cfg.Cids.Count}");
        Console.WriteLine($"[onco] Threads: {cfg.Threads} | Timeout: {cfg.Timeout}s | Retries: {cfg.Retries}\n");

        var sw=System.Diagnostics.Stopwatch.StartNew();
        var result=new Dictionary<string,object>();
        var errors=new List<string>();

        Console.WriteLine("Etapa 1/3 – Totais por região …");
        Merge(result,await EtapaTotaisAsync(cfg,errors));

        Console.WriteLine("Etapa 2/3 – Faixas etárias …");
        Merge(result,await EtapaFaixasAsync(cfg,errors));

        Merge(result,await EtapaCidsAsync(cfg,errors));

        await File.WriteAllTextAsync(
            cfg.OutputFile,
            JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),
            Encoding.UTF8);

        Console.WriteLine($"\n✓ JSON salvo em {Path.GetFullPath(cfg.OutputFile)}");
        Console.WriteLine($"Tempo total: {sw.Elapsed.TotalSeconds:F1}s");
        if(errors.Count>0) errors.ForEach(e=>Console.WriteLine(" • "+e));
    }
    
    static Config AskUser()
    {
        List<int> anos;
        while(true)
        {
            try{
                Console.Write("Ano inicial (2013‑2025): "); int a=int.Parse(Console.ReadLine()!);
                Console.Write("Ano final   (2013‑2025): "); int b=int.Parse(Console.ReadLine()!);
                if(a<2013||a>2025||b<2013||b>2025) throw new();
                if(b<a) (a,b)=(b,a);
                anos=Enumerable.Range(a,b-a+1).ToList(); break;
            }catch{Console.WriteLine("❌  Valores inválidos.");}
        }
        int AskInt(string msg,int def){Console.Write($"{msg} [{def}]: ");var s=Console.ReadLine()!;
            return string.IsNullOrWhiteSpace(s)?def:int.Parse(s);}
        int thr=AskInt("Threads",24), tmo=AskInt("Timeout (s)",45), ret=AskInt("Retries",3);

        Console.Write("Faixas (, ou * ) [*]: "); var fl=Console.ReadLine()!;
        var fx=string.IsNullOrWhiteSpace(fl)||fl.Trim()=="*"?Const.AGE_GROUPS.Keys.ToList():
               fl.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).ToList();

        Console.Write("CIDs   (, ou * ) [*]: "); var cl=Console.ReadLine()!;
        var cd=string.IsNullOrWhiteSpace(cl)||cl.Trim()=="*"?Const.CODES_DETALHADOS:
               cl.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).ToList();

        Console.Write("Arquivo JSON [total_onco.json]: "); var arq=Console.ReadLine()!;
        if(string.IsNullOrWhiteSpace(arq)) arq="total_onco.json";

        return new Config(anos,fx,cd,thr,tmo,ret,arq);
    }


    static string BuildPayload(int ano,string sexo,string? faixa,string? cid)
    {
        string p=
            "Linha=Regi%E3o+-+resid%EAncia%7CSUBSTR%28CO_MUNICIPIO_RESIDENCIA%2C1%2C1%29%7C1"+
            "%7Cterritorio%5Cbr_regiao.cnv&Coluna=--N%E3o-Ativa--&Incremento=Casos%7C%3D+count%28*%29"+
            $"&PAno+do+diagn%F3stico={ano}%7C{ano}%7C4&XRegi%E3o+-+resid%EAncia=TODAS_AS_CATEGORIAS__"+
            "&XRegi%E3o+-+diagn%F3stico=TODAS_AS_CATEGORIAS__&XRegi%E3o+-+tratamento=TODAS_AS_CATEGORIAS__"+
            "&XUF+da+resid%EAncia=TODAS_AS_CATEGORIAS__&XUF+do+diagn%F3stico=TODAS_AS_CATEGORIAS__"+
            "&XUF+do+tratamento=TODAS_AS_CATEGORIAS__&SRegi%E3o+de+Saude+-+resid%EAncia=TODAS_AS_CATEGORIAS__"+
            "&SRegi%E3o+de+Saude+-+diagn%F3stico=TODAS_AS_CATEGORIAS__&SRegi%E3o+de+Saude+-+tratamento=TODAS_AS_CATEGORIAS__"+
            "&SMunic%ED%ADpio+da+resid%EAncia=TODAS_AS_CATEGORIAS__&SMunic%ED%ADpio+do+diagn%F3stico=TODAS_AS_CATEGORIAS__"+
            "&SMunic%ED%ADpio+do+tratamento=TODAS_AS_CATEGORIAS__&XDiagn%F3stico=TODAS_AS_CATEGORIAS__"+
            "&XDiagn%F3stico+Detalhado=TODAS_AS_CATEGORIAS__"+
            $"&XSexo={Const.SEXPARAM[sexo]}&XFaixa+et%E1ria=TODAS_AS_CATEGORIAS__&XIdade=TODAS_AS_CATEGORIAS__"+
            "&XM%EAs%2FAno+do+diagn%F3stico=TODAS_AS_CATEGORIAS__"+
            "&nomedef=PAINEL_ONCO%2FPAINEL_ONCOLOGIABR.def&grafico=";

        if(faixa!=null)
            p=p.Replace("&XFaixa+et%E1ria=TODAS_AS_CATEGORIAS__",
                        $"&XFaixa+et%E1ria={Const.AGE_GROUPS[faixa]}");
        if(cid!=null)
            p=p.Replace("&XDiagn%F3stico+Detalhado=TODAS_AS_CATEGORIAS__",
                        $"&XDiagn%F3stico+Detalhado={cid}%7C{cid}%7C3");
        return p;
    }

    static Dictionary<string,double>? ParseHtml(string html)
    {
        var m=Const.RE_ADDROWS.Match(html);
        if(!m.Success) return null;
        return Const.RE_LINHA.Matches(m.Groups[1].Value)
                 .ToDictionary(
                     r=>Const.REGIONS[int.Parse(r.Groups[1].Value)],
                     r=>double.Parse(r.Groups[2].Value,CultureInfo.InvariantCulture));
    }

    static async Task<Dictionary<string,double>?> FetchAsync(
        int ano,string sexo,string? faixa,string? cid,
        Config cfg,List<string> errors)
    {
        string payload=BuildPayload(ano,sexo,faixa,cid);
        for(int att=1;att<=cfg.Retries;att++)
        {
            using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(cfg.Timeout));
            try{
                using var content=new StringContent(payload,Encoding.UTF8,"application/x-www-form-urlencoded");
                using var resp=await http.PostAsync(Const.URL_POST,content,cts.Token);
                resp.EnsureSuccessStatusCode();
                return ParseHtml(await resp.Content.ReadAsStringAsync());
            }
            catch when(att<cfg.Retries){await Task.Delay(300+Random.Shared.Next(700));}
            catch(Exception ex){errors.Add($"{ano}-{sexo}-{faixa??"ALL"}-{cid??"TOT"} :: {ex.Message}");}
        }
        return null;
    }


    static async Task<Dictionary<string,object>> EtapaTotaisAsync(Config cfg,List<string> errors)
    {
        var d=new Dictionary<string,object>();
        var jobs=cfg.Years.SelectMany(a=>new[]{"ALL","M","F"},(a,sx)=>(a,sx)).ToList();
        await RunJobsAsync(jobs,cfg,errors,d,
            j=>FetchAsync(j.a,j.sx,null,null,cfg,errors),
            (j,reg,val)=>GetDict(d,j.a.ToString(),reg,"totais")[j.sx]=val);
        return d;
    }

    static async Task<Dictionary<string,object>> EtapaFaixasAsync(Config cfg,List<string> errors)
    {
        var d=new Dictionary<string,object>();
        var jobs=cfg.Years.SelectMany(a=>new[]{"ALL","M","F"},(a,sx)=>(a,sx))
                          .SelectMany(t=>cfg.Faixas,(t,fx)=>(t.a,t.sx,fx)).ToList();
        await RunJobsAsync(jobs,cfg,errors,d,
            j=>FetchAsync(j.a,j.sx,j.fx,null,cfg,errors),
            (j,reg,val)=>GetDict(d,j.a.ToString(),reg,j.fx,"totaisCID")[j.sx]=val);
        return d;
    }

    static async Task<Dictionary<string,object>> EtapaCidsAsync(Config cfg,List<string> errors)
    {
        var d=new Dictionary<string,object>();
        var jobs=cfg.Years.SelectMany(a=>new[]{"ALL","M","F"},(a,sx)=>(a,sx))
                          .SelectMany(t=>cfg.Faixas,(t,fx)=>(t.a,t.sx,fx))
                          .SelectMany(t=>cfg.Cids,(t,cd)=>(t.a,t.sx,t.fx,cd)).ToList();

        long done=0,tot=jobs.Count,step=Math.Max(1,(long)Math.Ceiling(tot*Const.PROGRESS_STEP));
        Console.WriteLine($"Etapa 3/3 – CIDs detalhados … ({tot:N0} consultas)");

        await RunJobsAsync(jobs,cfg,errors,d,
            j=>FetchAsync(j.a,j.sx,j.fx,j.cd,cfg,errors),
            (j,reg,val)=>GetDict(d,j.a.ToString(),reg,j.fx,j.cd)[j.sx]=val,
            ()=>
            { var d_=Interlocked.Increment(ref done);
              if(d_%step==0||d_==tot)Console.Write($"\r  {d_:N0}/{tot:N0}  ({d_*100.0/tot,5:F1} %) concluído");});

        Console.WriteLine("\n  → 100 % concluído.");
        return d;
    }


    static async Task RunJobsAsync<T>(
        List<T> jobs, Config cfg, List<string> errors, Dictionary<string,object> root,
        Func<T,Task<Dictionary<string,double>?>> fetch,
        Action<T,string,double> store,
        Action? progress=null)
    {
        var sem=new SemaphoreSlim(cfg.Threads);
        var tasks=jobs.Select(async job=>{
            await sem.WaitAsync();
            try{
                var r=await fetch(job);
                if(r!=null) lock(root) foreach(var (reg,val) in r) store(job,reg,val);
            }finally{sem.Release(); progress?.Invoke();}
        });
        await Task.WhenAll(tasks);
    }


    static Dictionary<string,object> GetDict(Dictionary<string,object> root,params string[] path)
    {
        var cur=root;
        foreach(var k in path)
        {
            if(!cur.TryGetValue(k,out var n)||n is not Dictionary<string,object> d)
                cur[k]=d=new Dictionary<string,object>();
            cur=d;
        }
        return cur;
    }
    static void Merge(Dictionary<string,object> dst,Dictionary<string,object> src)
    {
        foreach(var (k,v) in src) dst[k]=v is Dictionary<string,object> d?
            MergeNew(dst.TryGetValue(k,out var e)&&e is Dictionary<string,object> ed?ed:new(),d)
          :v;

        static Dictionary<string,object> MergeNew(Dictionary<string,object> a,Dictionary<string,object> b)
        { foreach(var (k,v) in b) a[k]=v is Dictionary<string,object> d?
                MergeNew(a.TryGetValue(k,out var e)&&e is Dictionary<string,object> ed?ed:new(),d):v; return a;}
    }
}