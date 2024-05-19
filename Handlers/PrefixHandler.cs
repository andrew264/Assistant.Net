﻿using Assistant.Net.Utils;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Assistant.Net.Handlers;

public class PrefixHandler
{
    private readonly CommandService _commands;
    private readonly DiscordSocketClient _discord;
    private readonly IServiceProvider _services;
    private readonly Config _config;

    public PrefixHandler(IServiceProvider services, Config config)
    {
        _commands = services.GetRequiredService<CommandService>();
        _discord = services.GetRequiredService<DiscordSocketClient>();
        _services = services;
        _config = config;

        _commands.CommandExecuted += CommandExecutedAsync;
        _discord.MessageReceived += MessageReceivedAsync;
    }

    public async Task InitializeAsync()
    {
        await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
    }

    public async Task MessageReceivedAsync(SocketMessage rawMessage)
    {
        // Ignore system messages, or messages from other bots
        if (!(rawMessage is SocketUserMessage message))
            return;
        if (message.Source != MessageSource.User)
            return;

        var argPos = 0;
        if (!(message.HasCharPrefix(_config.client.prefix[0], ref argPos) || message.HasMentionPrefix(_discord.CurrentUser, ref argPos)))
            return;

        var context = new SocketCommandContext(_discord, message);
        await _commands.ExecuteAsync(context, argPos, _services);
    }

    public async Task CommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
    {
        if (!command.IsSpecified)
            return;

        if (result.IsSuccess)
            return;

        await context.Channel.SendMessageAsync($"error: {result}");
    }
}