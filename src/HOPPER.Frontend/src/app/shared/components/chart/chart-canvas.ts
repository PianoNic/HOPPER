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
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  ChartConfiguration,
  ChartOptions,
  LinearScale,
  Plugin,
  Tooltip,
  TooltipItem,
} from 'chart.js';
import { ThemeService } from '../../services/theme.service';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip);

Chart.defaults.font.family =
  "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif";
Chart.defaults.font.size = 11;

/* Status hues are reserved and deliberately not themed: the same four steps clear 3:1 on both
   surfaces, so a state never changes colour when the dashboard does. */
export const CHART_STATUS = {
  good: '#0ca30c',
  neutral: '#898781',
  serious: '#ec835a',
} as const;

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

export type ChartFactory = (tokens: ChartTokens) => ChartConfiguration<'bar'>;

type TooltipConfig = NonNullable<NonNullable<ChartOptions<'bar'>['plugins']>['tooltip']>;

export function tooltipStyle(
  tokens: ChartTokens,
  label: (item: TooltipItem<'bar'>) => string,
): TooltipConfig {
  return {
    backgroundColor: tokens.tooltipSurface,
    titleColor: tokens.tooltipInk,
    bodyColor: tokens.tooltipInk,
    displayColors: false,
    padding: 8,
    cornerRadius: 6,
    callbacks: {
      title: () => '',
      label,
    },
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

/* Every bar carries its own value, so the value axis is dropped entirely rather than doubled up
   with gridlines. The label goes past the bar end when it measures small enough to fit there and
   inside the end when it does not, so it is never clipped by the plot edge. */
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

/* An interior stacked segment has no free end to hang a label off, so the label only goes in when
   the segment measures wide enough for it. The legend beside the chart carries every count anyway,
   which is what keeps the sub-3:1 status fills readable on the light surface. */
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

/* Chart.js sizes the canvas from its parent and only shrinks again when the parent is the sizing
   authority, so the host carries the box and the canvas is left without dimensions of its own.
   Giving the canvas a width of its own instead leaves it stuck at its widest and clipped by the
   card, which silently swallows the labels drawn past the bar ends. */
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

  private chart: Chart<'bar'> | null = null;

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
