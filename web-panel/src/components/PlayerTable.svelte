<script lang="ts">
  interface Player {
    steamId: string
    name: string
    ping: number
    connectedSince: string
    violationScore: number
  }

  let players: Player[] = $state([
    { steamId: '76561198012345678', name: 'PlayerOne', ping: 24, connectedSince: '2h ago', violationScore: 0 },
    { steamId: '76561198087654321', name: 'RustLord', ping: 56, connectedSince: '45m ago', violationScore: 12 },
    { steamId: '76561198011112222', name: 'SneakyBeaky', ping: 38, connectedSince: '5m ago', violationScore: 3 },
    { steamId: '76561198033334444', name: 'BaseBuilder', ping: 12, connectedSince: '8h ago', violationScore: 0 },
  ])

  function handleAction(action: string, player: Player) {
    // Placeholder for action handlers
    console.log(`${action}: ${player.name}`)
  }
</script>

<div class="space-y-3 md:space-y-0">
  <!-- Mobile card layout -->
  <div class="md:hidden space-y-3">
    {#each players as player}
      <div class="card-mobile space-y-3">
        <div class="flex items-center justify-between">
          <div>
            <p class="font-semibold">{player.name}</p>
            <p class="text-xs text-sentinel-secondary font-mono mt-0.5">{player.steamId}</p>
          </div>
          <span class="inline-flex items-center rounded-full bg-white/5 px-2 py-0.5 text-xs font-medium text-sentinel-secondary">
            {player.ping}ms
          </span>
        </div>
        <div class="flex items-center justify-between text-sm">
          <span class="text-sentinel-secondary">Connected {player.connectedSince}</span>
          {#if player.violationScore > 0}
            <span class="text-sentinel-danger font-medium">{player.violationScore} flags</span>
          {:else}
            <span class="text-sentinel-success font-medium">Clean</span>
          {/if}
        </div>
        <div class="flex gap-2 pt-1">
          <button
            class="touch-target flex-1 rounded-md bg-white/5 px-3 py-2 text-sm font-medium transition hover:bg-white/10"
            onclick={() => handleAction('warn', player)}
          >
            Warn
          </button>
          <button
            class="touch-target flex-1 rounded-md bg-white/5 px-3 py-2 text-sm font-medium transition hover:bg-white/10"
            onclick={() => handleAction('kick', player)}
          >
            Kick
          </button>
          <button
            class="touch-target flex-1 rounded-md bg-sentinel-danger/10 px-3 py-2 text-sm font-medium text-sentinel-danger transition hover:bg-sentinel-danger/20"
            onclick={() => handleAction('ban', player)}
          >
            Ban
          </button>
        </div>
      </div>
    {/each}
  </div>

  <!-- Desktop table layout -->
  <div class="hidden md:block overflow-hidden rounded-lg border border-white/5">
    <table class="w-full text-left text-sm">
      <thead class="bg-sentinel-surface text-xs uppercase tracking-wide text-sentinel-secondary">
        <tr>
          <th class="px-4 py-3 font-medium">Name</th>
          <th class="px-4 py-3 font-medium">Steam ID</th>
          <th class="px-4 py-3 font-medium">Ping</th>
          <th class="px-4 py-3 font-medium">Connected</th>
          <th class="px-4 py-3 font-medium">Flags</th>
          <th class="px-4 py-3 font-medium text-right">Actions</th>
        </tr>
      </thead>
      <tbody class="divide-y divide-white/5">
        {#each players as player}
          <tr class="transition hover:bg-white/[0.02]">
            <td class="px-4 py-3 font-medium">{player.name}</td>
            <td class="px-4 py-3 font-mono text-xs text-sentinel-secondary">{player.steamId}</td>
            <td class="px-4 py-3">{player.ping}ms</td>
            <td class="px-4 py-3 text-sentinel-secondary">{player.connectedSince}</td>
            <td class="px-4 py-3">
              {#if player.violationScore > 0}
                <span class="text-sentinel-danger font-medium">{player.violationScore}</span>
              {:else}
                <span class="text-sentinel-success">0</span>
              {/if}
            </td>
            <td class="px-4 py-3">
              <div class="flex justify-end gap-2">
                <button
                  class="touch-target rounded-md bg-white/5 px-3 py-1.5 text-xs font-medium transition hover:bg-white/10"
                  onclick={() => handleAction('warn', player)}
                >
                  Warn
                </button>
                <button
                  class="touch-target rounded-md bg-white/5 px-3 py-1.5 text-xs font-medium transition hover:bg-white/10"
                  onclick={() => handleAction('kick', player)}
                >
                  Kick
                </button>
                <button
                  class="touch-target rounded-md bg-sentinel-danger/10 px-3 py-1.5 text-xs font-medium text-sentinel-danger transition hover:bg-sentinel-danger/20"
                  onclick={() => handleAction('ban', player)}
                >
                  Ban
                </button>
              </div>
            </td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
</div>
