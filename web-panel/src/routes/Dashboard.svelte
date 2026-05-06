<script lang="ts">
  import Navigation from '../components/Navigation.svelte'
  import StatsCard from '../components/StatsCard.svelte'
  import PlayerTable from '../components/PlayerTable.svelte'

  interface Props {
    token?: string | null
    onLogout?: () => void
  }

  let { onLogout }: Props = $props()

  let tab = $state('overview')

  const tabs = [
    { id: 'overview', label: 'Overview' },
    { id: 'players', label: 'Players' },
    { id: 'bans', label: 'Bans' },
    { id: 'logs', label: 'Logs' },
    { id: 'config', label: 'Config' },
  ]
</script>

<div class="min-h-screen bg-sentinel-bg">
  <Navigation onLogout={onLogout} />

  <main class="mx-auto max-w-7xl px-4 py-6">
    <!-- Tab bar (mobile scrollable, desktop inline) -->
    <div class="mb-6 flex gap-1 overflow-x-auto pb-1 no-scrollbar">
      {#each tabs as t}
        <button
          class="touch-target shrink-0 rounded-md px-4 py-2 text-sm font-medium transition {tab === t.id ? 'bg-sentinel-accent text-white' : 'text-sentinel-secondary hover:bg-white/5 hover:text-sentinel-primary'}"
          onclick={() => tab = t.id}
        >
          {t.label}
        </button>
      {/each}
    </div>

    {#if tab === 'overview'}
      <div class="space-y-6">
        <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatsCard label="Online Players" value="42" change="+3" tone="success" />
          <StatsCard label="Active Bans" value="7" change="-1" tone="neutral" />
          <StatsCard label="AI Flags" value="12" change="+5" tone="danger" />
          <StatsCard label="Server FPS" value="120" change="stable" tone="neutral" />
        </div>

        <div class="card-mobile">
          <h2 class="mb-3 text-base font-semibold">Recent Activity</h2>
          <ul class="space-y-2 text-sm">
            <li class="flex items-start gap-3">
              <span class="mt-0.5 h-2 w-2 shrink-0 rounded-full bg-sentinel-success"></span>
              <span class="text-sentinel-secondary">Player <span class="text-sentinel-primary font-medium">RustLord</span> connected from EU</span>
            </li>
            <li class="flex items-start gap-3">
              <span class="mt-0.5 h-2 w-2 shrink-0 rounded-full bg-sentinel-danger"></span>
              <span class="text-sentinel-secondary">Ban issued by <span class="text-sentinel-primary font-medium">AdminOne</span> for cheating</span>
            </li>
            <li class="flex items-start gap-3">
              <span class="mt-0.5 h-2 w-2 shrink-0 rounded-full bg-sentinel-accent"></span>
              <span class="text-sentinel-secondary">AI triage flagged <span class="text-sentinel-primary font-medium">SneakyBeaky</span> with 87% confidence</span>
            </li>
          </ul>
        </div>
      </div>
    {:else if tab === 'players'}
      <div class="space-y-4">
        <div class="flex items-center gap-3">
          <input
            type="text"
            placeholder="Search players…"
            class="touch-target w-full rounded-md border border-white/10 bg-sentinel-surface px-3 py-2 text-sm placeholder:text-sentinel-secondary/50 focus:border-sentinel-accent focus:outline-none focus:ring-1 focus:ring-sentinel-accent"
          />
        </div>
        <PlayerTable />
      </div>
    {:else}
      <div class="card-mobile text-center py-12">
        <p class="text-sentinel-secondary text-sm">This section is coming soon.</p>
      </div>
    {/if}
  </main>
</div>
