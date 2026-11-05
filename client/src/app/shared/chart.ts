import { Component, input } from '@angular/core';
import { ApexOptions, ChartComponent } from 'ng-apexcharts';

/** Takes one ApexOptions object, so chart configs can live in plain functions. */
@Component({
  selector: 'app-chart',
  imports: [ChartComponent],
  template: `
    @let o = options();
    <apx-chart
      [chart]="o.chart!"
      [series]="$any(o.series)"
      [xaxis]="o.xaxis!"
      [yaxis]="o.yaxis!"
      [labels]="o.labels!"
      [colors]="o.colors!"
      [dataLabels]="o.dataLabels!"
      [stroke]="o.stroke!"
      [fill]="o.fill!"
      [legend]="o.legend!"
      [tooltip]="o.tooltip!"
      [grid]="o.grid!"
      [plotOptions]="o.plotOptions!"
      [markers]="o.markers!"
      [states]="o.states!"
      [noData]="o.noData!"
    />
  `,
})
export class Chart {
  readonly options = input.required<ApexOptions>();
}

export const CHART_FONT = '"Geist Variable", "Segoe UI Variable", sans-serif';
// Deep violet first, then the lavender swatches, then distinguishable supporting hues for multi-series charts.
export const PALETTE = [
  '#803FC6',
  '#D0A2F7',
  '#2F9A86',
  '#E3A13B',
  '#C2417F',
  '#4C86A8',
  '#A97BE0',
  '#7D8B5A',
];
export const LAVENDER = ['#F1EAFF', '#E5D4FF', '#DCBFFF', '#D0A2F7'];

export function baseChart(
  type: ApexChart['type'],
  height: number,
  onClick?: (index: number) => void,
): ApexChart {
  return {
    type,
    height,
    fontFamily: CHART_FONT,
    toolbar: { show: false },
    zoom: { enabled: false },
    animations: { enabled: true, speed: 350 },
    events: onClick
      ? { dataPointSelection: (_e, _c, cfg) => onClick(cfg?.dataPointIndex ?? 0) }
      : undefined,
  };
}

type ApexChart = NonNullable<ApexOptions['chart']>;
