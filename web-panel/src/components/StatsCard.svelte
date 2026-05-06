<script lang="ts">
  interface Props {
    label: string
    value: string | number
    change?: string
    tone?: 'neutral' | 'success' | 'danger'
  }

  let { label, value, change, tone = 'neutral' }: Props = $props()

  const toneClasses = {
    neutral: 'text-sentinel-secondary',
    success: 'text-sentinel-success',
    danger: 'text-sentinel-danger',
  }
</script>

<div class="card-mobile flex flex-col gap-1">
  <span class="text-xs font-medium uppercase tracking-wide text-sentinel-secondary">{label}</span>
  <div class="flex items-baseline gap-2">
    <span class="text-2xl font-bold">{value}</span>
    {#if change}
      <span class="text-sm font-medium {toneClasses[tone]}">{change}</span>
    {/if}
  </div>
</div>
