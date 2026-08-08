import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  input,
  viewChild,
} from '@angular/core';
import {
  ArcElement,
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  ChartConfiguration,
  ChartOptions,
  ChartType,
  DoughnutController,
  Filler,
  LineController,
  LineElement,
  LinearScale,
  Plugin,
  PointElement,
  Tooltip,
  TooltipItem,
} from 'chart.js';
import { ThemeService } from '../../services/theme.service';

Chart.register(
  ArcElement,
  BarController,
  BarElement,
  CategoryScale,
  DoughnutController,
  Filler,
  LineController,
  LineElement,
  LinearScale,
  PointElement,
  Tooltip,
);

Chart.defaults.font.family =
  "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif";
Chart.defaults.font.size = 11;

/* Not themed on purpose: all three clear 3:1 on either surface, so a state never changes colour
   when the dashboard does. */
export const CHART_STATUS = {
  good: '#0ca30c',
  neutral: '#898781',
  serious: '#ec835a',
} as const;

/* Okabe-Ito, which is the published qualitative set that stays separable under every common form
   of colour blindness. Nominal categories need distinct hues rather than a ramp, and picking them
   by eye is how two of them end up indistinguishable to a protanope. */
export const CHART_CATEGORY = ['#0072b2', '#e69f00', '#009e73', '#cc79a7', '#56b4e9'] as const;

export type ChartTokens = {
  readonly surface: string;
  readonly series: string;
  readonly ink: string;
  readonly tooltipSurface: string;
  readonly tooltipInk: string;
  readonly status: typeof CHART_STATUS;
};

const LIGHT: ChartTokens = {
  surface: '#ffffff',
  series: '#2a78d6',
  ink: '#52514e',
  tooltipSurface: '#0b0b0b',
  tooltipInk: '#ffffff',
  status: CHART_STATUS,
};

const DARK: ChartTokens = {
  surface: '#171717',
  series: '#3987e5',
  ink: '#c3c2b7',
  tooltipSurface: '#383835',
  tooltipInk: '#ffffff',
  status: CHART_STATUS,
};

export type ChartFactory = (tokens: ChartTokens) => ChartConfiguration;

/* Style only. The label callback stays at the call site so its item is typed by the chart it
   belongs to, which a shared generic cannot do without indexing an unresolved ChartOptions<T>. */
export function tooltipStyle(tokens: ChartTokens) {
  return {
    backgroundColor: tokens.tooltipSurface,
    titleColor: tokens.tooltipInk,
    bodyColor: tokens.tooltipInk,
    displayColors: false,
    padding: 8,
    cornerRadius: 6,
  };
}

function luminance(hex: string): number {
  const channel = (i: number): number => {
    const c = parseInt(hex.slice(1 + i * 2, 3 + i * 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * channel(0) + 0.7152 * channel(1) + 0.0722 * channel(2);
}

export function inkOn(fill: string): string {
  const l = luminance(fill);
  return (l + 0.05) / 0.05 > 1.05 / (l + 0.05) ? '#0b0b0b' : '#ffffff';
}

const LABEL_FONT = `600 11px ${Chart.defaults.font.family}`;

/* Drawn inside the bar end when it would otherwise be clipped by the plot edge. */
export function valueAtTip(tokens: ChartTokens, format: (value: number) => string): Plugin<'bar'> {
  return {
    id: 'hopperValueAtTip',
    afterDatasetsDraw(chart) {
      const meta = chart.getDatasetMeta(0);
      const values = chart.data.datasets[0].data as ReadonlyArray<number>;
      const { ctx, chartArea } = chart;

      ctx.save();
      ctx.font = LABEL_FONT;
      ctx.textBaseline = 'middle';

      meta.data.forEach((bar, i) => {
        const text = format(values[i]);
        const width = ctx.measureText(text).width;
        const outside = bar.x + 6 + width <= chartArea.right;

        ctx.fillStyle = outside ? tokens.ink : inkOn(tokens.series);
        ctx.textAlign = outside ? 'left' : 'right';
        ctx.fillText(text, outside ? bar.x + 6 : bar.x - 6, bar.y);
      });

      ctx.restore();
    },
  };
}

/* Skipped when the segment is too narrow to hold it; the legend carries every count regardless. */
export function countInSegment(): Plugin<'bar'> {
  return {
    id: 'hopperCountInSegment',
    afterDatasetsDraw(chart) {
      const { ctx } = chart;

      ctx.save();
      ctx.font = LABEL_FONT;
      ctx.textBaseline = 'middle';
      ctx.textAlign = 'center';

      chart.data.datasets.forEach((dataset, index) => {
        const bar = chart.getDatasetMeta(index).data[0];
        if (!bar) return;

        const text = `${(dataset.data as ReadonlyArray<number>)[0]}`;
        const props = bar.getProps(['x', 'y', 'base'], true) as {
          x: number;
          y: number;
          base: number;
        };
        const span = Math.abs(props.x - props.base);
        if (ctx.measureText(text).width + 12 > span) return;

        ctx.fillStyle = inkOn(`${dataset.backgroundColor}`);
        ctx.fillText(text, (props.x + props.base) / 2, props.y);
      });

      ctx.restore();
    },
  };
}

/* The host carries the box and the canvas gets no dimensions of its own: Chart.js only shrinks a
   canvas back down when the parent is the sizing authority. Size the canvas instead and it sticks
   at its widest, clipped by the card, silently swallowing the labels past the bar ends. */
@Component({
  selector: 'app-chart-canvas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'relative block size-full' },
  template: `<canvas #canvas></canvas>`,
})
export class ChartCanvas {
  readonly build = input.required<ChartFactory>();

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly theme = inject(ThemeService);

  private chart: Chart | null = null;

  constructor() {
    afterRenderEffect(() => {
      const config = this.build()(this.theme.resolved() === 'dark' ? DARK : LIGHT);

      this.release();
      this.chart = new Chart(this.canvas().nativeElement, config);
    });

    inject(DestroyRef).onDestroy(() => this.release());
  }

  private release(): void {
    this.chart?.destroy();
    this.chart = null;
  }
}
