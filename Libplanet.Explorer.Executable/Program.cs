using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bencodex.Types;
using Cocona;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Blocks;
using Libplanet.Crypto;
using Libplanet.Explorer.Executable.Exceptions;
using Libplanet.Explorer.Interfaces;
using Libplanet.Explorer.Store;
using Libplanet.Net;
using Libplanet.Net.Protocols;
using Libplanet.RocksDBStore;
using Libplanet.Store;
using Libplanet.Store.Trie;
using Libplanet.Tx;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using NetMQ;
using Serilog;
using Serilog.Events;

namespace Libplanet.Explorer.Executable
{
    /// <summary>
    /// The program entry point to run a web server.
    /// </summary>
    public class Program : CoconaLiteConsoleAppBase
    {
        public static async Task Main(string[] args)
        {
            await CoconaLiteApp.RunAsync<Program>(args);
        }

        [Command(Description = "Run libplanet-explorer with options.")]
        [SuppressMessage(
            "Microsoft.StyleCop.CSharp.ReadabilityRules",
            "MEN003",
            Justification = "Many lines are required for running the method.")]
        public async Task Run(
            [Option(
                "store-path",
                new[] { 'P' },
                Description = @"The path of the blockchain store. If omitted (default)
in memory version is used.")]
            string storePath,
            [Option(
                "store-type",
                new[] { 'T' },
                Description = @"The type of the blockchain store. If omitted (default)
in DefaultStore is used.")]
            string storeType,
            [Option(
                "genesis-block",
                new[] { 'G' },
                Description = "The path of the genesis block. It should be absolute or http url.")]
            string genesisBlockPath,
            [Option("debug", new[] { 'd' }, Description = "Print logs for debugging as well.")]
            bool debug = false,
            [Option("host", new[] { 'H' }, Description = "The host address to listen.")]
            string host = "0.0.0.0",
            [Option("port", new[] { 'p' }, Description = "The port number to listen.")]
            int port = 5000,
            [Option(
                "block-interval",
                new[] { 'i' },
                Description = @"An appropriate interval in milliseconds between
consecutive blocks.")]
            int blockIntervalMilliseconds = 5000,
            [Option(
                "minimum-difficulty",
                new[] { 'm' },
                Description = "Allowed minimum difficulty for mining blocks.")]
            long minimumDifficulty = 1024L,
            [Option(
                "difficulty-bound-divisor",
                new[] { 'D' },
                Description = "A bound divisor to determine precision of block difficulties.")]
            int difficultyBoundDivisor = 128,
            [Option(
                "workers",
                new[] { 'W' },
                Description = "The number of swarm workers.")]
            int workers = 50,
            [Option(
                "app-protocol-version",
                new[] { 'V' },
                Description = "An app protocol version token.")]
            string appProtocolVersionToken = null,
            [Option(
                "mysql-server",
                Description = "A hostname of MySQL server.")]
            string mysqlServer = null,
            [Option(
                "mysql-port",
                Description = "A port of MySQL server.")]
            uint? mysqlPort = null,
            [Option(
                "mysql-username",
                Description = "The name of MySQL user.")]
            string mysqlUsername = null,
            [Option(
                "mysql-password",
                Description = "The password of MySQL user.")]
            string mysqlPassword = null,
            [Option(
                "mysql-database",
                Description = "The name of MySQL database to use.")]
            string mysqlDatabase = null,
            [Option(
                "max-transactions-per-block",
                Description = @"The number of maximum transactions able to be included
in a block.")]
            int maxTransactionsPerBlock = 100,
            [Option(
                "max-block-bytes",
                Description = @"The number of maximum bytes size of blocks except
for genesis block.")]
            int maxBlockBytes = 100 * 1024,
            [Option(
                "max-genesis-bytes",
                Description = "The number of maximum bytes size of the genesis block.")]
            int maxGenesisBytes = 1024 * 1024,
            [Option(
                "seed",
                new[] { 's' },
                Description = @"Seed nodes to join to the network as a node. The format of each
seed is a comma-separated triple of a peer's hexadecimal public key, host, and port number.
E.g., `02ed49dbe0f2c34d9dff8335d6dd9097f7a3ef17dfb5f048382eebc7f451a50aa1,example.com,31234'.
If omitted (default) explorer only the local blockchain store.")]
            string[] seedStrings = null,
            [Option(
                "ice-server",
                new[] { 'I' },
                Description = "URL to ICE server (TURN/STUN) to work around NAT.")]
            string iceServerUrl = null
        )
        {
            Options options = new Options(
                debug,
                host,
                port,
                blockIntervalMilliseconds,
                minimumDifficulty,
                difficultyBoundDivisor,
                workers,
                appProtocolVersionToken,
                mysqlServer,
                mysqlPort,
                mysqlUsername,
                mysqlPassword,
                mysqlDatabase,
                maxTransactionsPerBlock,
                maxBlockBytes,
                maxGenesisBytes,
                seedStrings,
                iceServerUrl,
                storePath,
                storeType,
                genesisBlockPath);

            var loggerConfig = new LoggerConfiguration();
            loggerConfig = options.Debug
                ? loggerConfig.MinimumLevel.Debug()
                : loggerConfig.MinimumLevel.Information();
            loggerConfig = loggerConfig
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console();
            Log.Logger = loggerConfig.CreateLogger();

            try
            {
                IRichStore store = LoadStore(options);
                IStateStore stateStore = new TrieStateStore(new RocksDBKeyValueStore(
                    System.IO.Path.Combine(options.StorePath, "states")));

                IBlockPolicy<NullAction> policy =
                    new DumbBlockPolicy(LoadBlockPolicy<NullAction>(options));
                IStagePolicy<NullAction> stagePolicy =
                    new VolatileStagePolicy<NullAction>();
                var blockChain =
                    new BlockChain<NullAction>(
                        policy,
                        stagePolicy,
                        store,
                        stateStore,
                        options.GetGenesisBlock(policy));
                Startup.PreloadedSingleton = false;
                Startup.BlockChainSingleton = blockChain;
                Startup.StoreSingleton = store;

                IWebHost webHost = WebHost.CreateDefaultBuilder()
                    .UseStartup<ExplorerStartup<NullAction, Startup>>()
                    .UseSerilog()
                    .UseUrls($"http://{options.Host}:{options.Port}/")
                    .Build();

                Swarm<NullAction> swarm = null;
                if (!(options.Seeds is null))
                {
                    string aggregatedSeedStrings =
                        options.SeedStrings.Aggregate(string.Empty, (s, s1) => s + s1);
                    Console.Error.WriteLine(
                        $"Seeds are {aggregatedSeedStrings}");

                    // TODO: Take privateKey as a CLI option
                    // TODO: Take appProtocolVersion as a CLI option
                    // TODO: Take host as a CLI option
                    // TODO: Take listenPort as a CLI option
                    if (options.IceServer is null)
                    {
                        Console.Error.WriteLine(
                            "error: -s/--seed option requires -I/--ice-server as well."
                        );
                        Environment.Exit(1);
                        return;
                    }

                    Console.Error.WriteLine("Creating Swarm.");

                    var privateKey = new PrivateKey();

                    // FIXME: The appProtocolVersion should be fixed properly.
                    var swarmOptions = new SwarmOptions
                    {
                        Type = SwarmOptions.TransportType.NetMQTransport,
                        BootstrapOptions = new BootstrapOptions
                        {
                            SeedPeers = options.Seeds.ToImmutableList(),
                        },
                        TimeoutOptions = new TimeoutOptions
                        {
                            MaxTimeout = TimeSpan.FromSeconds(10),
                        },
                    };

                    swarm = new Swarm<NullAction>(
                        blockChain,
                        privateKey,
                        options.AppProtocolVersionToken is string t
                            ? AppProtocolVersion.FromToken(t)
                            : default(AppProtocolVersion),
                        differentAppProtocolVersionEncountered: (p, pv, lv) => { },
                        workers: options.Workers,
                        iceServers: new[] { options.IceServer },
                        options: swarmOptions
                    );
                }

                using (var cts = new CancellationTokenSource())
                using (swarm)
                {
                    Console.CancelKeyPress += (sender, eventArgs) =>
                    {
                        eventArgs.Cancel = true;
                        cts.Cancel();
                    };

                    try
                    {
                        await Task.WhenAll(
                            webHost.RunAsync(cts.Token),
                            StartSwarmAsync(swarm, cts.Token)

                        );
                    }
                    catch (OperationCanceledException)
                    {
                        await swarm?.StopAsync(waitFor: TimeSpan.FromSeconds(1))
                            .ContinueWith(_ => NetMQConfig.Cleanup(false));
                    }
                }
            }
            catch (InvalidOptionValueException e)
            {
                string expectedValues = string.Join(", ", e.ExpectedValues);
                Console.Error.WriteLine($"Unexpected value given through '{e.OptionName}'\n"
                          + $"  given value: {e.OptionValue}\n"
                          + $"  expected values: {expectedValues}");
            }
        }

        private static IRichStore LoadStore(Options options)
        {
            // FIXME: This method basically does the same thing to Libplanet.Extensions.Cocona's
            // Utils.LoadStoreFromUri() method.
            // The duplicate code should be extract to a shared common method.
            // https://github.com/planetarium/libplanet/issues/1573
            bool readOnlyMode = options.Seeds is null;
            IStore innerStore;
            switch (options.StoreType)
            {
                case "rocksdb":
                    innerStore = new RocksDBStore.RocksDBStore(
                      options.StorePath,
                      maxTotalWalSize: 16 * 1024 * 1024,
                      keepLogFileNum: 1);
                    break;
                case "default":
                    innerStore = new DefaultStore(
                        options.StorePath,
                        flush: false,
                        readOnly: readOnlyMode);
                    break;
                default:
                    // FIXME: give available store type as argument hint without code duplication.
                    var availableStoreTypes = new[] { "rocksdb", "default" };
                    const string longOptionName = "store-type";
                    throw new InvalidOptionValueException(
                        "--" + longOptionName,
                        options.StoreType,
                        availableStoreTypes);
            }

            bool useMySQL = !string.IsNullOrEmpty(options.MySQLDatabase) &&
                            !string.IsNullOrEmpty(options.MySQLPassword) &&
                            !string.IsNullOrEmpty(options.MySQLServer) &&
                            !string.IsNullOrEmpty(options.MySQLUsername) &&
                            !(options.MySQLPort is null);
            if (useMySQL)
            {
                var mySqlOptions = new MySQLRichStoreOptions(
                    options.MySQLDatabase,
                    options.MySQLServer,
                    options.MySQLPort.Value,
                    options.MySQLUsername,
                    options.MySQLPassword);

                var store = new MySQLRichStore(
                    innerStore,
                    mySqlOptions
                );
                store.Migrate();
                return store;
            }
            else
            {
                return new LiteDBRichStore(
                    innerStore,
                    path: options.StorePath,
                    flush: false,
                    readOnly: readOnlyMode
                );
            }
        }

        private static BlockPolicy<T> LoadBlockPolicy<T>(Options options)
            where T : IAction, new()
        {
            return new BlockPolicy<T>(
                blockAction: null,
                blockInterval: TimeSpan.FromMilliseconds(options.BlockIntervalMilliseconds),
                difficultyStability: options.DifficultyBoundDivisor,
                minimumDifficulty: options.MinimumDifficulty,
                getMaxBlockBytes: i => i > 0 ? options.MaxBlockBytes : options.MaxGenesisBytes,
                getMaxTransactionsPerBlock: _ => options.MaxTransactionsPerBlock);
        }

        private static async Task StartSwarmAsync(
            Swarm<NullAction> swarm,
            CancellationToken cancellationToken)
        {
            if (swarm is null)
            {
                Startup.PreloadedSingleton = true;
                return;
            }

            try
            {
                Console.Error.WriteLine("Bootstrapping.");
                await swarm.BootstrapAsync(cancellationToken);
            }
            catch (TimeoutException)
            {
                Console.Error.WriteLine("No any neighbors.");
            }

            Console.Error.WriteLine("Starts preloading.");
            await swarm.PreloadAsync(cancellationToken: cancellationToken);
            Console.Error.WriteLine("Finished preloading.");
            Console.Error.WriteLine("Migrate to MySQL.");
            Startup.PreloadedSingleton = true;

            await swarm.StartAsync(cancellationToken: cancellationToken);
        }

        internal class DumbBlockPolicy : IBlockPolicy<NullAction>
        {
            private readonly IBlockPolicy<NullAction> _impl;

            public DumbBlockPolicy(BlockPolicy<NullAction> blockPolicy)
            {
                _impl = blockPolicy;
            }

            public IAction BlockAction => _impl.BlockAction;

            public IComparer<IBlockExcerpt> CanonicalChainComparer =>
                _impl.CanonicalChainComparer;

            public int GetMinTransactionsPerBlock(long index) =>
                _impl.GetMinTransactionsPerBlock(index);

            public int GetMaxTransactionsPerBlock(long index) =>
                _impl.GetMaxTransactionsPerBlock(index);

            public long GetMaxBlockBytes(long index) =>
                _impl.GetMaxBlockBytes(index);

            public long GetNextBlockDifficulty(BlockChain<NullAction> blocks)
            {
                return 0;
            }

            public TxPolicyViolationException ValidateNextBlockTx(
                BlockChain<NullAction> blockChain, Transaction<NullAction> transaction)
            {
                return _impl.ValidateNextBlockTx(blockChain, transaction);
            }

            public BlockPolicyViolationException ValidateNextBlock(
                BlockChain<NullAction> blockChain, Block<NullAction> nextBlock
            )
            {
                return _impl.ValidateNextBlock(blockChain, nextBlock);
            }

            public HashAlgorithmType GetHashAlgorithm(long index) =>
                _impl.GetHashAlgorithm(index);

            public int GetMaxTransactionsPerSignerPerBlock(long index) =>
                _impl.GetMaxTransactionsPerSignerPerBlock(index);
        }

        internal class Startup : IBlockChainContext<NullAction>
        {
            public bool Preloaded => PreloadedSingleton;

            public BlockChain<NullAction> BlockChain => BlockChainSingleton;

            public IStore Store => StoreSingleton;

            internal static bool PreloadedSingleton { get; set; }

            internal static BlockChain<NullAction> BlockChainSingleton { get; set; }

            internal static IStore StoreSingleton { get; set; }
        }

        private class NoOpStateStore : IStateStore
        {
            public ITrie GetStateRoot(HashDigest<SHA256>? stateRootHash) => null;

            public void PruneStates(IImmutableSet<HashDigest<SHA256>> survivingStateRootHashes)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
