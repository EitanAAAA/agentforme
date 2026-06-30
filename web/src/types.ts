export type CandleDto = {
  symbol: string
  timestamp: string
  open: number
  high: number
  low: number
  close: number
  volume: number
}

export type ChartAnnotationDto = {
  kind: string
  direction: 'Bullish' | 'Bearish' | string
  startTimestamp: string
  endTimestamp: string
  price: number
  secondaryPrice?: number | null
  tertiaryPrice?: number | null
  label: string
  isTriggered: boolean
}

export type BosSignalDto = {
  swingTimestamp: string
  breakTimestamp: string
  swingPrice: number
  breakClose: number
  direction: string
  label: string
}

export type FvgZoneDto = {
  startTimestamp: string
  endTimestamp: string
  lower: number
  upper: number
  direction: string
  label: string
}

export type IfvgZoneDto = FvgZoneDto

export type HalfBoxDto = {
  startTimestamp: string
  endTimestamp: string
  level0: number
  level05: number
  level1: number
  direction: string
}

export type MockTradeBoxDto = {
  startTimestamp: string
  endTimestamp: string
  entry: number
  stopLoss: number
  takeProfit: number
  direction: string
  isTriggered: boolean
}

export type SmtEventDto = {
  id: string
  timestamp: string
  setupType: string
  direction: string
  status: string
  leaderSymbol: string
  failedSymbol: string
  reason: string
  workflowState: string
  calculationSummary: string
  detectionMode: string
  esCurrentValue: number
  nqCurrentValue: number
  esCurrentTimestamp: string
  nqCurrentTimestamp: string
  esPreviousSwingValue: number
  nqPreviousSwingValue: number
  esPreviousSwingTimestamp: string
  nqPreviousSwingTimestamp: string
  esFvgLower?: number | null
  esFvgUpper?: number | null
  esFvgStartTimestamp?: string | null
  esFvgEndTimestamp?: string | null
  nqFvgLower?: number | null
  nqFvgUpper?: number | null
  nqFvgStartTimestamp?: string | null
  nqFvgEndTimestamp?: string | null
}

export type SmtEventCanceledDto = {
  id: string
  reason: string
}

export type NqOneMinuteAnalysisDto = {
  smtEventId: string
  windowStart: string
  windowEnd: string
  candles: CandleDto[]
  bosSignals: BosSignalDto[]
  fvgZones: FvgZoneDto[]
  ifvgZones: IfvgZoneDto[]
  halfBox?: HalfBoxDto | null
  mockTradeBox?: MockTradeBoxDto | null
  annotations: ChartAnnotationDto[]
  summary: string
}

export type AppSettingsDto = {
  timeframe: string
  detectionMode: string
  swingStrength: number
  refreshRateSeconds: number
  alertCooldownMinutes: number
  alertCooldownCandles: number
  tickTolerance: number
  tickSize: number
  riskBufferPoints: number
  showBullishSmt: boolean
  showBearishSmt: boolean
  dataProvider: string
}

export type DataStatusDto = {
  status: string
  dataStatus: string
  dataProvider: string
  lastUpdated?: string | null
  message: string
}
