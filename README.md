---
**Achtung**
Dies ist der Quelltext, welcher für den Blog Artikel (https://blog.bajonczak.com/durableFunctions/) erläutert wurde.
Im Folgenden kopiere ich den Inhalt aus dem Blog
---

# Durable Functions
Dieser Artikel setzt die Grundlagen der Serverlosen Azure Functions vorraus. Druable Functions ist eine erweiterung für die Azure Functions. Diese erlaubt es zustandsbehafte Funktionen in einer Serverlosen landschaft zu implementieren. Dies bedeutet das beispielsweise Workflowprozesse in Auzure functions realisierbar sind. Ich will diesen Artikel anhand eines Beispiels erläutern. Jedoch muss ich mich auf einen Durable Functions Typ beschränken, da dies den Rahmen des Artikels sprengen würde. 

Als kleine Information vorab. Der Code ist auch auf [Github](https://github.com/SBajonczak/azuredurable) verfügbar.

# Mein Problem
Ich hatte vor kurzem das Problem das bei Kunden die Importe (Dateiupload und verarbeitung) zu lange andauern. Ich habe dies meist damit umgangen indem ich die Dateien zuerst irgendwohin (meist Storage Account) hochlade, und dann über einen Zeitgeberauftrag die Dateien sequenziell abarbeite. 

Ich hätte das dieses mal auch wieder gemacht, jedoch finde ich das der Benutzer damit keine Übersicht hat, wie der aktuelle Status des __Jobs__ ist. 

Schematisch sollte der Prozess wie folgt ablaufen

<mermaid>
sequenceDiagram
    participant Benutzer
    participant System
    participant Speicher
    Benutzer->>System: Dateiupload
    System->>Speicher: Datei wird mit einer eindeutigen Kennung abgelegt
    System->> Benutzer: Rückgabe Antwort mit Möglichkeit zur Abfrage des aktuellen Auftragsstatus
    System->System: Asynchrones verarbeiten der Datei und ergebnis protokollieren.
</mermaid>

Quasi erhält der Benutzer nach dem Dateiupload direkt ein Feedback. Dieses beinhaltet Adressen mit dem man in der LAge ist, den Status des aktuellen Jobs abzufragen, oder aber auch abzubrechen. 

So ist es möglich dem Benutzer auch ein optisches Feedback uzu geben, denn wenn der Status nicht aussagt, das er erfolgreich beendet ist. Dann kann der Import Button noch deaktiviert sein, das am besten auch mit einem entsprechenden Hinweis. 

# Typen
Nun ist mein Problem bekannt, jedoch wie können Durable Functions dabei helfen? Wie wir wissen können wir mit Durable Functions Statusbehaftete Prozesse nun auch in einer Serverlosen architektur behandeln. Aber zuerst würde ich ganz kurz auf die Typen der Durable Functions eingehen. Denn es gibt dabei unterschiedliche Entwurfsmuster. 

### Fan Out / Fan In

Beim Muster Auffächern auswärts/einwärts werden mehrere Funktionen parallel ausgeführt und anschließend auf den Abschluss aller gewartet. Häufig werden die von den Funktionen zurückgegebenen Ergebnisse einer Aggregation unterzogen.

Bei normalen Funktionen kann das Fan out erfolgen, indem die Funktion mehrere Nachrichten an eine Warteschlange sendet. Das Fan In ist wesentlich schwieriger. Für das Fan In wird mit Hilfe eines Function App Code nachverfolgt, wann die von der Warteschlange ausgelösten Funktionen enden und speichert dann ihre Ausgaben. Dies ist ähnlich dem [Aggregator Pattern](https://www.enterpriseintegrationpatterns.com/patterns/messaging/Aggregator.html) für Fan In und den [Splitter Pattern](https://www.enterpriseintegrationpatterns.com/patterns/messaging/Sequencer.html) für das Fan out.

```c#
[FunctionName("FanOutFanIn")]
public static async Task Run(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    var parallelTasks = new List<Task<int>>();

    // Get a list of N work items to process in parallel.
    object[] workBatch = await context.CallActivityAsync<object[]>("F1", null);
    for (int i = 0; i < workBatch.Length; i++)
    {
        Task<int> task = context.CallActivityAsync<int>("F2", workBatch[i]);
        parallelTasks.Add(task);
    }

    await Task.WhenAll(parallelTasks);

    // Aggregate all N outputs and send the result to F3.
    int sum = parallelTasks.Sum(t => t.Result);
    await context.CallActivityAsync("F3", sum);
}
```

### Functions Chaining (Funktionsverkettung)
Beim Muster der Funktionsverkettung wird eine Abfolge von Funktionen in einer bestimmten Reihenfolge ausgeführt. Bei diesem Muster wird die Ausgabe einer Funktion als Eingabe einer weiteren Funktion verwendet.


Wie in der Abbildung zu sehen, können mit diesen Anwendungsmusert unterschiedliche Funktionen miteinander verkettet werden. 

In diesem Beispiel sind die Werte F1, F2, F3 und F4 die Namen weiterer Funktionen in der Funktions-App. Der Ablauf wird dann ganz klassisch imperativ durchgeführt, der Aufruf der Functions erfolgt über den Orchestirerungskontext. 

Beispielcode
```c#
[FunctionName("Chaining")]
public static async Task<object> Run(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    try
    {
        var x = await context.CallActivityAsync<object>("F1", null);
        var y = await context.CallActivityAsync<object>("F2", x);
        var z = await context.CallActivityAsync<object>("F3", y);
        return  await context.CallActivityAsync<object>("F4", z);
    }
    catch (Exception)
    {
        // Error handling or compensation goes here.
    }
}
```
Was ist hierbei der vorteil gegenüber einer Function App die dann lediglich alles sequentziell abarbeitet?

Ganz einfach, bei jedem __await__ wird eine Art Prüfpunkt erstellt. Das bedeutet, das wenn der Aufruf, bedingt durch Neustarts der Azure Function App, abgebrochen wird, dann wird der Prozess ab den letzten await neugestartet. So dass nicht alles redundant ausgeführt wird. 

### Asynchrone HTTP-APIs
Das asynchrone HTTP-API-Muster ist geeignet, um den Status von Vorgängen mit langer Ausführungsdauer mit externen Clients zu koordinieren. Ein gängiges Verfahren zum Implementieren dieses Musters besteht darin, die Aktion mit langer Ausführungsdauer von einem HTTP-Endpunkt auslösen zu lassen. 

Nachdem das initiieren einers HTTP Aufrufs erfoglt, liefert die API eine Antwort mit div. Enpunkte. Ein Endpunkt davon bietet die Möglichkeit zur Abfrage des aktuellen Auftragsstatus.
Der Ablauf stellt sich auf API-Ebene wie folgt dar:

1. Zuerst der Initiale Aufruf der Start Function App

```bash
> curl -X POST https://myfunc.azurewebsites.net/orchestrators/DoWork -H "Content-Length: 0" -i
HTTP/1.1 202 Accepted
Content-Type: application/json
Location: https://myfunc.azurewebsites.net/runtime/webhooks/durabletask/b79baf67f717453ca9e86c5da21e03ec

{
    "id": "b79baf67f717453ca9e86c5da21e03ec",
    "statusQueryGetUri": "https://myfunc.azurewebsites.net/runtime/webhooks/durabletask/instances/b79baf67f717453ca9e86c5da21e03ec?taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA==",
    "sendEventPostUri": "https://myfunc.azurewebsites.net/runtime/webhooks/durabletask/instances/b79baf67f717453ca9e86c5da21e03ec/raiseEvent/{eventName}?taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA==",
    "terminatePostUri": "https://myfunc.azurewebsites.net/runtime/webhooks/durabletask/instances/b79baf67f717453ca9e86c5da21e03ec/terminate?reason={text}&taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA==",
    "rewindPostUri": "https://myfunc.azurewebsites.net/runtime/webhooks/durabletask/instances/b79baf67f717453ca9e86c5da21e03ec/rewind?reason={text}&taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA==",
    "purgeHistoryDeleteUri": "https://myfunc.azurewebsites.net/runtime/webhooks/durabletask/instances/b79baf67f717453ca9e86c5da21e03ec?taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA=="
}
```
Unter anderem wird nicht nur die ID zurückgegeben, sondern auch Eine Adresse zur Abfrage des Status, der Wert steht in __statusQeuryUri__.
Dieser wird nun verwendet, um den Auftragsstatus abzufragen.

2. Abfrage des Status

```bash

> curl https://myfunc.azurewebsites.net/runtime/webhooks/durabletask/instances/b79baf67f717453ca9e86c5da21e03ec?taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA== -i
HTTP/1.1 202 Accepted
Content-Type: application/json
Location: https://myfunc.azurewebsites.net/runtime/webhooks/durabletask/instances/b79baf67f717453ca9e86c5da21e03ec?taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA==

{"runtimeStatus":"Running","lastUpdatedTime":"2019-03-16T21:20:47Z", ...}
```

Ich habe die Ausgabe nun etwas verkürzt. Ersichtlich ist aber der __runtimeStatus__ hier steht drin wie der aktuelle Status des Auftrags ist. Da er noch im Running Prozess ist, muss auf die finalisierung etwas gewaret werden. Daher wird nach einer Weile der Status erneut abgefrgat.

3. Erneute Abfrage des Status

```bash
> curl https://myfunc.azurewebsites.net/runtime/webhooks/durabletask/instances/b79baf67f717453ca9e86c5da21e03ec?taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA== -i
HTTP/1.1 200 OK
Content-Length: 175
Content-Type: application/json

{"runtimeStatus":"Completed","lastUpdatedTime":"2019-03-16T21:20:57Z", ...}
```

Nun ist der __runtimeStatus__ auf __Completed__ und man kann sichergehen das es ohne Probleme ausgeführt wurde, andernfalls würde dort __Failed__ im Ergebnis stehen.

### Überwachen (Monitoring)
Das Überwachen-Muster bezieht sich auf einen flexiblen, wiederkehrenden Vorgang in einem Workflow. Ein Beispiel besteht im Abfragen, bis bestimmte Bedingungen erfüllt sind. Wir können einen normalen Timertrigger für ein einfaches Szenario verwenden, beispielsweise einen periodischen Bereinigungsauftrag. Sein Intervall ist jedoch statisch, und die Verwaltung der Instanzlebensdauer wird komplex. Mithilfe von Durable Functions können flexible Wiederholungsintervalle erstellen, die Lebensdauer von Aufgaben verwalten und mehrere Überwachungsprozesse aus einer einzelnen Orchestrierung erstellen.

Ein Beispiel für das Überwachen-Muster besteht in der Umkehrung des früheren asynchronen HTTP-API-Szenarios. Anstatt einen Endpunkt für einen externen Client freizugeben, um einen langlaufenden Vorgang zu überwachen, belegt der lang laufende Monitor einen externen Endpunkt und wartet dann auf einen Zustandswechsel.

Folgender Beispielcode soll aufzeigen, wie ein Monitoring Job aussehen kann.

```c#
[FunctionName("MonitorJobStatus")]
public static async Task Run(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    int jobId = context.GetInput<int>();
    int pollingInterval = GetPollingInterval();
    DateTime expiryTime = GetExpiryTime();

    while (context.CurrentUtcDateTime < expiryTime)
    {
        var jobStatus = await context.CallActivityAsync<string>("GetJobStatus", jobId);
        if (jobStatus == "Completed")
        {
            // Perform an action when a condition is met.
            await context.CallActivityAsync("SendAlert", machineId);
            break;
        }

        // Orchestration sleeps until this time.
        var nextCheck = context.CurrentUtcDateTime.AddSeconds(pollingInterval);
        await context.CreateTimer(nextCheck, CancellationToken.None);
    }

}
```

Wenn eine Anforderung empfangen wird, wird eine neue Orchestrierungsinstanz für diese Auftrags-ID erstellt. Die Instanz fragt den Status ab, bis eine Bedingung erfüllt ist und die Schleife beendet wird. Ein permanenter Timer steuert das Abrufinterval. Anschließend können weitere Arbeitsschritte ausgeführt werden, oder die Orchestrierung wird beendet. Falls __expiryTime__ von __nextCheck__ überschritten wird, wird der Monitor beendet.

### Benutzerinteraktion (Approvals)
Viele automatisierte Prozesse enthalten eine Form der Benutzerinteraktion. Das Einbeziehen von Menschen in einen automatisierten Prozess ist schwierig, da Personen nicht im gleichen hohen Maß verfügbar und reaktionsfähig sind wie Clouddienste. Ein automatisierter Prozess kann diese Interaktion mithilfe von Zeitlimits und Kompensationslogik ermöglichen.

Ein Genehmigungsprozess ist ein Beispiel für einen Geschäftsprozesses, der Benutzerinteraktion umfasst. Beispielsweise kann für eine Spesenabrechnung, die einen bestimmten Betrag überschreitet, die Genehmigung eines Vorgesetzten erforderlich sein. Wenn der Vorgesetzte die Spesenabrechnung nicht innerhalb von 72 Stunden genehmigt (vielleicht weil er im Urlaub ist), wird ein Eskalationsverfahren wirksam, um die Genehmigung von einer anderen Person (z.B. dem Vorgesetzten des Vorgesetzten) zu erhalten.


In diesen Beispielen wird ein Genehmigungsprozess erstellt, um das Muster der Benutzerinteraktion zu veranschaulichen:
```c#
[FunctionName("ApprovalWorkflow")]
public static async Task Run(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    await context.CallActivityAsync("RequestApproval", null);
    using (var timeoutCts = new CancellationTokenSource())
    {
        DateTime dueTime = context.CurrentUtcDateTime.AddHours(72);
        Task durableTimeout = context.CreateTimer(dueTime, timeoutCts.Token);

        Task<bool> approvalEvent = context.WaitForExternalEvent<bool>("ApprovalEvent");
        if (approvalEvent == await Task.WhenAny(approvalEvent, durableTimeout))
        {
            timeoutCts.Cancel();
            await context.CallActivityAsync("ProcessApproval", approvalEvent.Result);
        }
        else
        {
            await context.CallActivityAsync("Escalate", null);
        }
    }
}
```

Der Code startet ein Genehmigungsprozess, der 72 Stunden auf eine Genehmigung (ApprovalEvent) wartet. Tritt dies ein, wird die Aktivität __ProcessApproval__ ausgeführt, passiert kein Approval oder tritt ein Timeout auf dann wird die Aktivität __Escalate__ initiiert.

Das Signal zur Genehmigung kann auf zwei verschiedene WEge erfolgen entweder direkt per HTTP Request

```bash
  curl -d "true" http://localhost:7071/runtime/webhooks/durabletask/instances/{instanceId}/raiseEvent/ApprovalEvent -H "Content-Type: application/json"
```

Alternativ kann hier eine separate Durable FunctionApp geschrieben werden, die dann die Aktivität __ApprovalEvent__ aufruf hier der Beispielcode dazu

```c#
[FunctionName("RaiseEventToOrchestration")]
public static async Task Run(
    [HttpTrigger] string instanceId,
    [DurableClient] IDurableOrchestrationClient client)
{
    bool isApproved = true;
    await client.RaiseEventAsync(instanceId, "ApprovalEvent", isApproved);
}
```

### Aggregator (zustandsbehaftete Entitäten)

Bei diesem Muster geht es um Aggregierung von Ereignisdaten über einen bestimmten Zeitraum in einer einzigen, adressierbaren Entität. In diesem Muster können die aggregierten Daten aus mehreren Quellen stammen, in Batches geliefert werden und über lange Zeiträume verteilt sein. Der Aggregator muss möglicherweise Aktionen für Ereignisdaten durchführen, und es kann sein, dass externe Daten die aggregierten Daten abgefragt werden müssen.

Das Schwierige an der Implementierung dieses Musters mit normalen zustandslosen Funktionen, ist die Tatsache, dass das Steuern der Parallelität zur Herausforderung wird. Es muss sich nicht nur um mehrere Threads, die gleichzeitig dieselben Daten anpassen, gekümmert werden. Es muss auch dafür gesorg werden, dass der Aggregator immer nur auf einer Instanz (Singelton) ausgeführt wird.

Folgender Code spiegelt so eine Aggregator Pattern wieder

```c#
[FunctionName("Counter")]
public static void Counter([EntityTrigger] IDurableEntityContext ctx)
{
    int currentValue = ctx.GetState<int>();
    switch (ctx.OperationName.ToLowerInvariant())
    {
        case "add":
            int amount = ctx.GetInput<int>();
            ctx.SetState(currentValue + amount);
            break;
        case "reset":
            ctx.SetState(0);
            break;
        case "get":
            ctx.Return(currentValue);
            break;
    }
}
```
Obiges Beispiel spiegelt eine Implementierung wider, welche eine recht simple Function App Implementierung hervorruft. Die Aurufbaren Aktionen werden ganz simpel mit einem Case identifiziert und somit entsprechend abgearbeitet.

Alternativ kann das ganze auch als Entity selbst gebaut werden in dem sich die Function befindet. 

```c#
public class Counter
{
    [JsonProperty("value")]
    public int CurrentValue { get; set; }

    public void Add(int amount) => this.CurrentValue += amount;

    public void Reset() => this.CurrentValue = 0;

    public int Get() => this.CurrentValue;

    [FunctionName(nameof(Counter))]
    public static Task Run([EntityTrigger] IDurableEntityContext ctx)
        => ctx.DispatchAsync<Counter>();
}
```

So wird alles in der Entität selbst geregelt. Somit werden auch die aufgerufenen Aktionen entsprechend mit einem Dispatcher umgelenkt auf die bereitgestellten Methoden.

Jetzt haben wir den Trigger definiert. Doch wie wird dieser aufgerufen?
Dies zeigt folgendes Beispiel:

```c#
[FunctionName("EventHubTriggerCSharp")]
public static async Task Run(
    [EventHubTrigger("device-sensor-events")] EventData eventData,
    [DurableClient] IDurableOrchestrationClient entityClient)
{
    var metricType = (string)eventData.Properties["metric"];
    var delta = BitConverter.ToInt32(eventData.Body, eventData.Body.Offset);

    // The "Counter/{metricType}" entity is created on-demand.
    var entityId = new EntityId("Counter", metricType);
    await entityClient.SignalEntityAsync(entityId, "add", delta);
}
```

Hier wird eine EntityID generiert, in dem der AzureFunctoin __Counter__ und den Metrikwert angegeben wird.
Über den Orchestrationsclient wird dann mit der Entitäts ID die auszuführende Methode inkl. dem Wert bekannt gegeben. 
Die Azure Function __Counter__ wird aufgerufen, anschließend die entsprechnde Methode in der Function usgewertet und mit dem Wert ausgeführt. 
In dem Beispiel wird ein Wert addiert. 

# Lösung für mein Problem
Nun, da die Anwendungsmuster bekannt sind, ist auch klar welches Anwendungsmuster ich verwende. 
Nochmal zur Erinnerung, ich lade eine Datei hoch und muss warten bis die Daten importiert sind. 
Somit habe ich ein langlaufenden Http Prozess. Mein Bestreben ist es diesen, asynchron zu gestalten. 

Mit diesen Anforderungen steht fest das ich eine [Asnychrone API](./#asynchrone-http-apis) implementieren muss.

# Implementation
Da ich keinen Quelltext vom Originellen System zeigen darf ([NDA](https://de.wikipedia.org/wiki/Geheimhaltungsvertrag) Seitens Kunden), habe ich mir erlaubt, einfach ein Beispiel nachzubauen, damit klar wird wie das ganze implementiert wird. 

## Einrichtung
Als Grundlage __muss__ Azure Functions mindestens in Version 2.0 verwendet werden. Ich Arbeite in [VisalStudio Code](https://code.visualstudio.com/) dort ist die [Azure Functions Erweiterung](https://marketplace.visualstudio.com/items?itemName=ms-azuretools.vscode-azurefunctions) bei mir Installiert. 

Zum Erstellen einfach F1 drücken und ein neues Azure Function Projekt erstellen. Beim erstellen des Prpjekt überspringe ich das erstellen der ersten Function. 

Nun kann scho mit F5 das Azure Functionapps Projekt gestartet werden. Jedoch ist dies relativ sinnlos da wir nichts zum Ausführen haben. 

Daher erstellen wir nun die erste Durable Function. 
Hier ist über F1->"Azure Function: Create Function..." dann die "Durable Function Orchestration" zu erstellen.


> Bei der anlage wird nach einem Storage Account gefragt. Dieser ist wichtig, da der Status der einzelnen Prozesse dort abgelegt wird. So auch die Historie der einzelnen Task.

## Erstellen der Durable Function
Nun haben wir ein FunctionApp Projekt, welches eine Lauffähige Durable Function haben. 

Diese ist wie folgt aufgebaut

1. HTTP Methode die vom Client angesprochen werden kann. 

```c#
        [FunctionName("Import_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("Import", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
```
Diese sieht aus wie eine normale HTTP Trigger Function App mit der besonderheit das es eine __DurableOrchestrationClient__ bindung gibt. Diese ist notwendig, um eine Asynchrone Instanz zustarten. Die Function App gibt auch ein JSOn mit Enpunkte zurück, mit dessen Hilfe dann ein Status abgefragt werden kann (wie oben bereits erläutert).

2.Die Orchestrator Funktion

```c#
        [FunctionName("Import")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var outputs = new List<string>();

            // Replace "hello" with the name of your Durable Activity Function.
            outputs.Add(await context.CallActivityAsync<string>("Import_Hello", "Tokyo"));
            outputs.Add(await context.CallActivityAsync<string>("Import_Hello", "Seattle"));
            outputs.Add(await context.CallActivityAsync<string>("Import_Hello", "London"));

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
            return outputs;
        }
```

Diese Function wird von der Start Function (HTTP) als asynchrone Instanz ausgeführt. Hier werden die, wie der Name es schon verrät, die verschiedenen Auszuführenden Aktionen orchestriert. So kann der Ablauf entsprechend gestaltet werden.

3. Aktivität
```c#

 [FunctionName("Import_Hello")]
        public static string SayHello([ActivityTrigger] string name, ILogger log)
        {
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }
```

Dies ist eine Aktivität welche die eigentliche Arbeit erleidgt. 

Also ist der Ablauf wie folgt. 

1. Client ruft die HTTP Function auf. 
2. Die Http Function erstellt eine neue Orchestierungs Instanz, welche Asynchron ausgeführt wird
3. gleichzeitig liefert die HTTP Funtion Endpunkte zur steuerung der Instanz zurück
4. Die asynchron ausgeführte Instanz führt die definierten Aktivitäten aus.

## Die Business Logik
Jetzt da wir unsere Function App haben muss nun noch die Logik mit hinein. Ich erwarte meist mehr als nur eine Datei, das möchte ich auch gern beibehalten. Jedoch ist im Beispiel nur ein Text (String) als Übergabewert vorhanden. Hier kann auch nur ein Textwert übergeben werden, dies ist eine Konvention, die festgelegt wurde.  

Doch ich brauch mehr als nur ein Text als Übergabeparameter. 
Was benötige ich 

1. Dateiname
2. E-Mail Adresse zur Benachrichtigung der Fertigstellung
3. Eventuelle Zusatzparameter aps Eigenschaft. In meinen Fall musste ich ein Paramter für das verwendete Halbjahr angeben. Daher habe ich diese Eigenschaft mit hineingenommen. 
4. Inhalt der Datei (Jedoch wird das nicht ind er Parameterübergabe mitgeliefert)

Die die Repräsentation einer Datei habe ich folgendes Objekt erstellt

```c#
namespace SBA.Durable
{
    public class ImportParameters
    {
        public string FileName { get; set; }

        public byte[] Content { get; set; }

    }
}
```
Ich habe aber noch mehr Parameter, da ich das nicht bei jeder Datei mit angeben will, habe ich noch ein Container Objekt erstellt, in dem die Informationen zur Datei vorhanden sind. 

```c#
using System.Collections.Generic;

namespace SBA.Durable.Parameters
{
    public class ImportParametersContainer
    {
        public List<ImportParameters> Importings { get; set; }
        public string NotifierMail { get; set; }
        public string InstannceId { get; set; }
        public ImportParametersContainer()
        {
            this.Importings = new List<ImportParameters>();
        }
        public ImportParametersContainer(List<ImportParameters> importing, string notifierMail) : this()
        {
            this.Importings = importing;
            this.NotifierMail = notifierMail;

        }

    }
}
```

Dieses Container Objekt verwende ich für die Übergabe zur Instanz, denn dies beinhaltet die Importierten Dateien und die E-Mail-Adresse des Benutzer, welcher den Import gestartet hat. So kann ich dann nach Abschluss des Imports, dem Benutzer eine E-Mail schreiben mit den Status des Erfolgt. 

Ich habe daraufhin die Startfunktion wie folgt angepasst.

```c#
 [FunctionName("Import_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            ILogger log)
        {

            ImportRequestParameter requestParameter = new ImportRequestParameter(req);
            List<ImportParameters> result = await requestParameter.UploadFilesToStorageAccountAndGnerateParameters();

            ImportParametersContainer parametersContainer = new ImportParametersContainer(result, "");
            parametersContainer.InstannceId = await requestParameter.GenerateInstanceID();

            string instanceId = await starter.StartNewAsync("Import", parametersContainer);


            return starter.CreateCheckStatusResponse(req, instanceId);
        }
```

Was hier noch zu beachten ist, ist das ich die Dateien aus dem Request extrahiere und in einem Storage Account hochlade. Dies geschieht über die __ImportRequestParameter__. Als Ergebnis erhalt eich eine Liste von __ImportParameters__ das ich dann zu der Container Instanz hinzufügen kann. Die Instanz habe ich dann bei der __StartNewAsync__ Methode übergeben. Dadurch das es ein Objekt ist, wird das nun automatsich in ein [JSON-Format](https://de.wikipedia.org/wiki/JavaScript_Object_Notation) serialisiert und so an die Orchestrierungsfunktion übergeben.

Das war quasi der schwierigste Teil. Ab jetzt wird es simpler... Denn jetzt ist in der Orchestrierungsfunktion nur noch das Objekt zu Deserialisieren. Das gelingt indem man einfach den State mit einem Typenparameter abruft.

```c#
  ImportParametersContainer data = context.GetInput<ImportParametersContainer>();
```

Dieses Objekt kann nun in die einzelnen Aktivitäten weitergetragen werden. 
Ich habe nun zwei Aktivitäten erstellt

1. zum Auslesen der Datei aus dem Storage und Import in das Ziel
2. Zum Versand der Mail

Die Import Funktion habe ich wie folgt erstellt. Hierbei habe ich die Logic weg gelassen die das Ziel beschreibt, das kann jeder selbst schreiben 😉.
Aber was zu sehe ist, ist das ich eine Liste von __ImportResult__ zurückgebe. Dort stehen Informationen drin, welche dann ausgeben wie der Import verlief. 

```c#

        [FunctionName("Import_FetchFile")]

        public static async Task<string> FetchFile([ActivityTrigger] ImportParameters parameter, ILogger log)
        {
            string constring = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            BlobServiceClient blobServiceClient = new BlobServiceClient(constring);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("imports");
            BlobClient blob = containerClient.GetBlobClient(parameter.FileName);

            MemoryStream s = new MemoryStream();
            blob.DownloadTo(s);
            // Set into parameterobject
            parameter.Content = s.ToArray();
            // Add Logic for Importing and replacing the following line
            List<ImportResult> returnValue= new List<ImportResult>();
            // Return the results
            return JsonConvert.SerializeObject(returnValue);
        }
```

Den E-Mailversand habe ich auch in einer eigenen Aktivät gesetzt. Auch hier habe ich die Logik herausgenommen, da viele entweder per SendGrid oder halt normal über Exchange versenden. Da kann jeder seine Logik einfügen, wie er es mag. 

```c#
        [FunctionName("Import_SendNotification")]

        public static async Task<string> SendNotification([ActivityTrigger] ImportParametersContainer parameter, ILogger log)
        {
            log.LogInformation("Sending information to requester");
            // Add Mail Logic here!
            return $"Mail to {parameter.NotifierMail} was send";
        }
```

Nun da ich beide Funktionen habe muss ich diese nur noch orchestrieren. Daher habe ich die Orchestrierungsfunktion wie folgt abgeändert
```c#
[FunctionName("Import")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            ImportParametersContainer data = context.GetInput<ImportParametersContainer>();
            var outputs = new List<string>();
            foreach (var importParmeter in data.Importings)
            {
                outputs.Add(await context.CallActivityAsync<string>("Import_FetchFile", importParmeter));
            }
            outputs.Add(await context.CallActivityAsync<string>("Import_SendNotification", data));
            return outputs;

        }
```
Ich rufe nun in einer Schleife die Importe auf und wenn alles fertig ist, versende ich einfch die E-Mail mit den entsprechenden Daten.
Ebenso wird die Ausgabe abgespeichert und zurück gegeben. Diese wird bei der Statusabfrge spätestens uzurückgeliefert. Denn man sieht zu jedem Step welche Aktivität ausgeführt wurde.

Nun da wir alles fertig haben, kann es auch schon ausgeführt werden. 

# Die erste Ausführung
Um das ganze auszuführen verwende ich Postman, der hat den Vorteil das man den Body, den Abfrageverb und die Antwort entsprechend vergeben und Formatieren kann.

Nachdem ich einen Aufruf an die HTTP Ausführung getätigt habe erhielt ich folgende Antwort

```json
{
    "id": "123",
    "statusQueryGetUri": "http://localhost:7071/runtime/webhooks/durabletask/instances/123?taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA==",
    "sendEventPostUri": "http://localhost:7071/runtime/webhooks/durabletask/instances/123/raiseEvent/{eventName}?taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA==",
    "terminatePostUri": "http://localhost:7071/runtime/webhooks/durabletask/instances/123/terminate?reason={text}&taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA==",
    "rewindPostUri": "http://localhost:7071/runtime/webhooks/durabletask/instances/123/rewind?reason={text}&taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA==",
    "purgeHistoryDeleteUri": "http://localhost:7071/runtime/webhooks/durabletask/instances/123?taskHub=DurableFunctionsHub&connection=Storage&code=ek9RYZRVOAGD91JUP099BYUXDq/o2XBYcZivPhnw26uMCEeyjBhjMA=="
}
```
Es schien geklappt zu haben. Was haben wir also erhalten? 

1. Die Ide der Instanz.
2. Endpunkt um den Status abzufragen
3. Endpunkt um ein Benutzerdefiniertes Ereigenis zu senden (in unserem Fall nicht notwendig)
4. Ein Endpunkt um den Prozess komplett abzubrechen (es wird kein Rollback durchgeführt)
5. Ein Endpunkt um den Prozess noch mal auszuführen
6. Ein Endpunkt um die Historiendaten zu entfernen 

Aktuell interessiert der Endpunkt für den Status, denn das ist, was wir brauchen, um den Asynchronen Prozess zu prüfen. Denn diese rkann länger dauern oder auch schon nach dem Absenden des Befehls fertig sein. Also fragen wir den mit der angegebenen Adresse einfach mal ab.

Als Ergebnis kommt folgendes zum Vorschein
```json

{
    "name": "Import",
    "instanceId": "123",
    "runtimeStatus": "Completed",
    "input": {
        "$type": "SBA.Durable.ImportParametersContainer, Azure-Function-Api",
        "Importings": [
            {
                "$type": "SBA.Durable.ImportParameters, Azure-Function-Api",
                "FileName": "Import.csv_ade444b895194e41aa8800252db792f8",
                "NotifierMail": null,
                "Content": null
            },
            {
                "$type": "SBA.Durable.ImportParameters, Azure-Function-Api",
                "FileName": "Zeitplandaten 2019-12-16 bis 2019-12-22.xlsx_1802da0de7374e2d9d38be5b1ae3ebe1",
                "HalfYearSetting": "123",
                "NotifierMail": null,
                "Content": null
            }
        ],
        "NotifierMail": "",
        "InstannceId": "123"
    },
    "customStatus": null,
    "output": [
        "[{\"FileName\":\"Import.csv_ade444b895194e41aa8800252db792f8\",\"ImportedRowCount\":0,\"TotalRowCount\":27,\"Messages\":null}]",
        "[{\"FileName\":\"Zeitplandaten 2019-12-16 bis 2019-12-22.xlsx_1802da0de7374e2d9d38be5b1ae3ebe1\",\"ImportedRowCount\":0,\"TotalRowCount\":38,\"Messages\":null}]",
        "Mail to  was send"
    ],
    "createdTime": "2019-12-21T10:00:24Z",
    "lastUpdatedTime": "2019-12-21T10:00:28Z"
}
```

Einmal sehen wir ganz oben den Namen der Funktion, darunter die ID der Instanz und direkt danach den Status. Daran erkennen wir, dass die Verarbeitung fertig ist. Es wird die Information ausgegeben wie der Aufrufparameter der Instanz ausgesehen hat. Auch wird die Ausgabe jeder einzelnen Aktivität im __output__ Feld hinterlegt. Jetzt ist auch ersichtlich, warum in der Orchestrierungsfuntktion ein Rückgabewert in form einer Liste erforderlich ist. Denn dies wird im __output__ Feld eingefügt.

Mit diesem Ergebnis kann im Frontend gearbeitet werden. So kann der Importbutton nun mit der abfrage auf den Status deaktiviert oder auch ausgeblendet werden, wenn der Prozess noch in Ausführung ist und wieder eingeblendet, wenn der Prozess schon abgeschlossen wurde. 

## Eigene Instanz Id
Oft ist es nicht hilfreich, dass eine automatisch generierte InstanzID erstellt wird. Bei dem Importer habe ich nämlich das Problem das ich nicht in einer Client Datenbank (Store o.ä.) die ID ablegen kann. Daher musste etwas anderes her. 

Zum Glück kann man eigene Instanz-ID's deifnieren. Doch hierbei gilt

> Der Entwickler ist dafür verantwortlich, dass eine eindeutigkeit der ID gewährleistet ist

Da ich aber Pro Benutzer nur ein Importprozess zulassen will habe ich mir gedacht, dass ich den Benutzernamen des aktuell angemeldeten Benutzers als Instanz ID nehme.

Doch wie klappt das?
Man kann jetzt im Header der Anfrage reinschauen, ob dort ein Token sich befindet, welches dann dekodiert werden muss. Alternativ kann man den Benutzer als Parameter mitgeben, dies ist aber nicht der beste weg. 

Doch zum Glück bietet Azure Functions ab der Version 2.0 die Möglichkeit den aktuellen Benutzer als __ClaimsPrincipal__-Bindung mitzugeben.
So wird der aktuelle Benutzer dort hinterlegt.  
```c#
[FunctionName("Import_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            ClaimsPrincipal principal,
            ILogger log)
        {

            ImportRequestParameter requestParameter = new ImportRequestParameter(req);
            List<ImportParameters> result = await requestParameter.UploadFilesToStorageAccountAndGnerateParameters();

            ImportParametersContainer parametersContainer = new ImportParametersContainer(result, "");
            // Get Username
            string customInstanceID= ((ClaimsIdentity)principal.Identity).Claims.First().Value;
            // Set id to instance id
            parametersContainer.InstanceId = customInstanceID;
            string instanceId = await starter.StartNewAsync("Import", parametersContainer.InstanceId,  parametersContainer);

   return starter.CreateCheckStatusResponse(req, instanceId);
        }
  ```

  ich gehe in meinem Beispiel davon aus das es ein ClaimsPrincipal ist. Denn in Azure werden in der Regel keine Basis Windows Credentials verwendet. Mit 

  ```c#
   ((ClaimsIdentity)principal.Identity).Claims.First().Value;
  ```

  Bekomme ich dann den aktuellen Benutzernamen raus. Normalerweise muss man den Claim direkt aufrufen, jedoch war in meinem Beispiel (lokal) der erste Claim der LoginName. 
  Diesen setze ich bei StartNewAsync als Parameter

  ```c#
    string instanceId = await starter.StartNewAsync("Import", parametersContainer.InstanceId,  parametersContainer);
  ```

  Damit habe ich nun eine eigene ID verwendet. Es liegt in der Natur eines Benutzernamens, dass dieser sich nicht ändert. Daher: 

  > Die Historiendaten müssen nach der vollständigen abarbeitung direkt gelöscht werden, damit die Instanz ID wiederverwendet werden kann.

  # Fazit
  Ich habe Azure Function Apps schon seit Anbegin verwendet. Damals waren diese als Erweiterung für die Azure Logic Apps vorgesehen, jedoch mausern sich diese, grade in Zeiten der Mikorsdienste, dazu eigene Selbständige Dienste zu werden. Mit der Durable erweiterung sind auch komplexere Prozesse möglich. In Unserem Beispiel habe ich auch aufgezeigt wie man ein Import asynchron durchführt. Dieser Prozess läuft komplett asynchron, jedohc kann der Frontendentwickler jederzeit den STatus abfragen, und muss sich selbst darum kümmern, dass der Prozess wieder bereinigt wird. Alternativ kann auch ein Watchdog dafür verwendet werden. Jedoch ist Proaktives Arbeiten effektiver 😉.
  
Ich habe das Projekt einmal bei [Github](https://github.com/SBajonczak/azuredurable) abgelegt.
