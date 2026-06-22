export type OverlayBox = {
  id: string
  kind: string
  label: string
  direction: string
  left: number
  top: number
  width: number
  height: number
  midY?: number
  level0Y?: number
  level1Y?: number
  color?: string
  opacity?: number
  dashed?: boolean
  lineWidth?: number
  selected?: boolean
  hidden?: boolean
}

export type OverlayLine = {
  id: string
  kind: string
  label: string
  x1: number
  x2: number
  y1: number
  y2: number
  color?: string
  opacity?: number
  dashed?: boolean
  lineWidth?: number
  selected?: boolean
  hidden?: boolean
}

type Props = {
  boxes: OverlayBox[]
  lines: OverlayLine[]
  mode: 'select' | 'draw'
  selectedId?: string | null
  onPointerDown: (event: PointerEvent<SVGSVGElement>) => void
  onPointerMove: (event: PointerEvent<SVGSVGElement>) => void
  onPointerUp: (event: PointerEvent<SVGSVGElement>) => void
  onSelect: (id: string, event: PointerEvent) => void
  onHandle: (id: string, handle: string, event: PointerEvent) => void
}

export function AnnotationLayer({
  boxes,
  lines,
  mode,
  selectedId,
  onPointerDown,
  onPointerMove,
  onPointerUp,
  onSelect,
  onHandle,
}: Props) {
  return (
    <svg
      className={`annotation-layer ${mode === 'draw' ? 'draw-mode' : ''}`}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerUp}
    >
      {boxes.filter((box) => !box.hidden).map((box) => (
        <g key={box.id} className="manual-shape" onPointerDown={(event) => onSelect(box.id, event)}>
          <rect
            x={box.left}
            y={box.top}
            width={box.width}
            height={box.height}
            className="annotation-hit-box"
          />
          <rect
            x={box.left}
            y={box.top}
            width={box.width}
            height={box.height}
            className={`annotation-box ${box.kind.toLowerCase()} ${box.direction.toLowerCase()} ${selectedId === box.id ? 'selected' : ''}`}
            stroke={box.color}
            strokeWidth={box.lineWidth ?? 1.4}
            strokeDasharray={box.dashed ? '6 4' : undefined}
            opacity={box.opacity ?? 1}
          />
          {box.level0Y !== undefined && <text x={box.left - 24} y={box.level0Y + 4} className="level-label">0</text>}
          {box.midY !== undefined && <text x={box.left - 33} y={box.midY + 4} className="level-label">0.5</text>}
          {box.level1Y !== undefined && <text x={box.left - 24} y={box.level1Y + 4} className="level-label">1</text>}
          {box.level0Y !== undefined && <text x={box.left + box.width + 7} y={box.level0Y + 4} className="level-label">0</text>}
          {box.midY !== undefined && <text x={box.left + box.width + 7} y={box.midY + 4} className="level-label">0.5</text>}
          {box.level1Y !== undefined && <text x={box.left + box.width + 7} y={box.level1Y + 4} className="level-label">1</text>}
          {box.midY !== undefined && <line x1={box.left} x2={box.left + box.width} y1={box.midY} y2={box.midY} className="mid-line" />}
          {box.label && <text x={box.left + 7} y={box.top + 18} className="zone-label">{box.label}</text>}
          {selectedId === box.id && <SelectionHandles box={box} onHandle={onHandle} />}
        </g>
      ))}
      {lines.filter((line) => !line.hidden).map((line) => (
        <g key={line.id} className="manual-shape" onPointerDown={(event) => onSelect(line.id, event)}>
          <line
            x1={line.x1}
            x2={line.x2}
            y1={line.y1}
            y2={line.y2}
            className="annotation-hit-line"
          />
          <line
            x1={line.x1}
            x2={line.x2}
            y1={line.y1}
            y2={line.y2}
            className={`annotation-line ${line.kind.toLowerCase()} ${selectedId === line.id ? 'selected' : ''}`}
            stroke={line.color}
            strokeWidth={line.lineWidth ?? 2}
            strokeDasharray={line.dashed ? '6 4' : undefined}
            opacity={line.opacity ?? 1}
          />
          {line.label && <text x={line.x2 + 6} y={line.y2 - 8} className="line-label">{line.label}</text>}
          {selectedId === line.id && (
            <>
              <circle cx={line.x1} cy={line.y1} r={5} className="handle" onPointerDown={(event) => onHandle(line.id, 'start', event)} />
              <circle cx={line.x2} cy={line.y2} r={5} className="handle" onPointerDown={(event) => onHandle(line.id, 'end', event)} />
            </>
          )}
        </g>
      ))}
    </svg>
  )
}

function SelectionHandles({ box, onHandle }: { box: OverlayBox; onHandle: Props['onHandle'] }) {
  const points = [
    ['nw', box.left, box.top],
    ['ne', box.left + box.width, box.top],
    ['sw', box.left, box.top + box.height],
    ['se', box.left + box.width, box.top + box.height],
  ] as const

  return (
    <>
      {points.map(([handle, x, y]) => (
        <circle key={handle} cx={x} cy={y} r={5} className="handle" onPointerDown={(event) => onHandle(box.id, handle, event)} />
      ))}
    </>
  )
}
import type { PointerEvent } from 'react'
