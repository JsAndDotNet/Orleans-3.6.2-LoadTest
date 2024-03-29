﻿// See https://aka.ms/new-console-template for more information
using System;
using System.Net;
using System.Threading.Tasks;
using Azure;
using LoadTest.Grains;
using LoadTest.SharedBase.Helpers;
using LoadTest.SharedBase.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;

Console.WriteLine("Starting Orleans Silo....");


DisplayHelper.WriteLine("This setup uses clustering. *** Remember to manually create a table called 'OrleansSiloInstances' in the appropriate azure table storage. ***", ConsoleColor.Yellow);

DisplayHelper.WriteLine("One storage per silo is faster, but can lose data if the silo goes down. A better strategy is needed!");
DisplayHelper.WriteLine("Use one storage account per silo? Y = yes, anything else = no");

bool useOneStoragePerSilo = Console.ReadKey().Key == ConsoleKey.Y;




try
{

    var clusterName = "dev"; //        - ALL NODES MUST HAVE THE SAME CLUSTER NAME!!!! (I.E. all 'dev', not 'dev1', 'dev2' etc)
                             // These 3 are only used to host on a different port
    var privateIp = "127.0.0.1";
    var siloPort = 11111; //           - The silo port should be individual to a silo
    var gatewayPort = 30000;// 22222; - The gateway should be the same for all



    var config = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddUserSecrets<Program>()
            .Build();


    var s = config.GetValue<string>("TestKey");

    // Load details of storage accounts for assigning to silos
    List<StorageConnectionInfo> grainStores = new List<StorageConnectionInfo>();

    for (int i = 0; i < 1000; i++)
    {
        var storageAcctName = config.GetValue<string>($"StorageAccount:{i}:AcctName");
        var sasToken = config.GetValue<string>($"StorageAccount:{i}:Sas");
        if (!String.IsNullOrEmpty(storageAcctName))
        {
            var conUrlFromSecret = $"https://{storageAcctName}.table.core.windows.net/";
            StorageConnectionInfo info = new StorageConnectionInfo(conUrlFromSecret, sasToken);
            grainStores.Add(info);
        }
        else
        {
            break;
        }
    }



    var consoleNumber = 0;
    while (consoleNumber <= 0)
    {
        DisplayHelper.WriteLine("Enter he number of this silo", ConsoleColor.Cyan);
        DisplayHelper.WriteLine("e.g. 1,2,3,4,5... We'll set up the values from there.", ConsoleColor.Cyan);
        var inputString = Console.ReadLine();

        if (int.TryParse(inputString, out consoleNumber))
        {
            siloPort = siloPort + (consoleNumber - 1);
        }
    }

    var clusterDetails = $"SILO: {clusterName} (port {siloPort})";
    Console.Title = clusterDetails;
    DisplayHelper.WriteLine(clusterDetails);


    // IMPORTANT: Distribute storage accounts evenly over the silos
    int storageIndex = 0;
    if(consoleNumber >= grainStores.Count())
    {
        storageIndex = consoleNumber % grainStores.Count();
    }
    else
    {
        storageIndex = consoleNumber % grainStores.Count() - 1;
    }

    StorageConnectionInfo grainStorageInfo = null;
    if (useOneStoragePerSilo)
    {
        // WARNING: This is fast, but doesn't handle silo's going down well 
        //          Can result in loss of data
        grainStorageInfo = grainStores[storageIndex]; // Even distribution (storage acct/silo) 
    }
    else
    {
        grainStorageInfo = grainStores[0]; // One Store
    }

 

    var clusterStorageAccountName = config.GetValue<string>("StorageAccount:Cluster:AccountName");
    var clusterStorageAccountUrlString = $"https://{clusterStorageAccountName}.table.core.windows.net/OrleansSiloInstances";
    var clusterStorageAccountSas = config.GetValue<string>("StorageAccount:Cluster:Sas");
    StorageConnectionInfo clusterStorageInfo = new StorageConnectionInfo(clusterStorageAccountUrlString, clusterStorageAccountSas);



    // define the cluster configuration
    var builder = new SiloHostBuilder()
        .AddAzureTableGrainStorage(
            name: "LoadTestNumbersTableStorage1",
            configureOptions: options =>
            {
                options.UseJson = true;
                options.ConfigureTableServiceClient(grainStorageInfo.StorageUri, grainStorageInfo.SasCredential);
            })
        //.UseDashboard(options => { }) -- Dashboard Hoofed for now (performance)
        //.UseLocalhostClustering()
        .UseAzureStorageClustering(options =>
        {
            options.ConfigureTableServiceClient(clusterStorageInfo.StorageUri, clusterStorageInfo.SasCredential);
            //options.TableName = "{whatever you call this, you need to add manually in table storage - default is 'OrleansSiloInstances'}";
        })
        .Configure<ClusterOptions>(options =>
        {
            options.ClusterId = clusterName;// "dev";
            options.ServiceId = "OrleansLoadTest";
        })
        // If we wanted to host on different port
        .ConfigureEndpoints(
            IPAddress.Parse(privateIp),
            siloPort: siloPort,
            gatewayPort: gatewayPort,
            listenOnAnyHostAddress: true)
        .Configure<GrainVersioningOptions>(options =>
        {
            options.DefaultCompatibilityStrategy =
            nameof(BackwardCompatible);
            options.DefaultVersionSelectorStrategy =
            nameof(MinimumVersion);
        })
        .ConfigureApplicationParts(parts => parts.AddApplicationPart(typeof(NumberStoreGrain).Assembly).WithReferences())
        .ConfigureLogging(logging =>
            logging.AddConsole().SetMinimumLevel(LogLevel.Error)
        );

    var host = builder.Build();
    await host.StartAsync();
    Console.WriteLine("\n\n Press Enter to terminate...\n\n");
    Console.ReadLine();

    await host.StopAsync();

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    return 1;
}


Console.WriteLine("\n\n Press Enter to terminate...\n\n");
Console.ReadLine();
Console.ReadLine();





